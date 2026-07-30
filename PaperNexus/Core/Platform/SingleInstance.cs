using System.Net.Sockets;
using System.Runtime.Versioning;

namespace PaperNexus.Core.Platform;

// Enforces one running copy of the app and lets a second launch tell the first to show
// its window. Named kernel objects do this on Windows; a Unix domain socket does it on
// Linux, where named EventWaitHandle throws PlatformNotSupportedException.
public interface ISingleInstance : IDisposable
{
    // True when this process took ownership and should run the app.
    public bool TryAcquire();

    // True when an already-running instance was found and told to show its window.
    public bool SignalExisting();

    // Blocks on the show-window channel until stopWhen returns true, invoking
    // onShowRequested for each signal received. Intended to be run on a background thread.
    public void Listen(Action onShowRequested, Func<bool> stopWhen);
}

public static class SingleInstance
{
    public static ISingleInstance Create()
    {
        if (OperatingSystem.IsWindows())
            return new WindowsSingleInstance();
        return new UnixSingleInstance();
    }
}

[SupportedOSPlatform("windows")]
internal sealed class WindowsSingleInstance : ISingleInstance
{
    private const string EventName = "PaperNexus_ShowUI";
    private const string MutexName = "PaperNexus_SingleInstance";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;

    public bool TryAcquire()
    {
        _mutex = new Mutex(false, MutexName);
        bool owned;
        try
        {
            owned = _mutex.WaitOne(0, exitContext: false);
        }
        catch (AbandonedMutexException)
        {
            owned = true; // previous instance crashed; we now own the mutex
        }

        if (!owned)
        {
            _mutex.Dispose();
            _mutex = null;
            return false;
        }

        // AutoReset: each Set() unblocks exactly one WaitOne().
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
        return true;
    }

    public bool SignalExisting()
    {
        if (!EventWaitHandle.TryOpenExisting(EventName, out var existingEvent))
            return false;
        using (existingEvent)
            existingEvent.Set();
        return true;
    }

    public void Listen(Action onShowRequested, Func<bool> stopWhen)
    {
        if (_showEvent is null)
            return;

        // Poll with a 1-second timeout so shutdown is noticed without blocking forever.
        while (!stopWhen())
        {
            try
            {
                if (_showEvent.WaitOne(1000) && !stopWhen())
                    onShowRequested();
            }
            catch (ObjectDisposedException)
            {
                break;
            }
        }
    }

    public void Dispose()
    {
        _showEvent?.Dispose();
        _showEvent = null;
        if (_mutex is not null)
        {
            try { _mutex.ReleaseMutex(); } catch (ApplicationException) { /* not owned */ }
            _mutex.Dispose();
            _mutex = null;
        }
    }
}

// A bound Unix domain socket doubles as the lock and the signalling channel: binding
// succeeds for exactly one process, and any later process can connect to it to ask the
// owner to show its window.
internal sealed class UnixSingleInstance : ISingleInstance
{
    private Socket? _listener;
    private string? _socketPath;

    // XDG_RUNTIME_DIR is per-user and cleared at logout, which is exactly the lifetime
    // wanted here. /tmp is the fallback when the session manager did not set it.
    private static string ResolveSocketPath()
    {
        var runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (string.IsNullOrEmpty(runtimeDir) || !Directory.Exists(runtimeDir))
            runtimeDir = Path.Combine(Path.GetTempPath(), $"papernexus-{Environment.UserName}");

        Directory.CreateDirectory(runtimeDir);
        return Path.Combine(runtimeDir, "PaperNexus.sock");
    }

    public bool TryAcquire()
    {
        _socketPath = ResolveSocketPath();

        // A socket file left behind by a crashed instance would block binding forever.
        // Probing it with a connect distinguishes "owner alive" from "stale file".
        if (File.Exists(_socketPath) && !CanConnect(_socketPath))
        {
            try { File.Delete(_socketPath); }
            catch (IOException) { return false; }
        }

        try
        {
            var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            listener.Bind(new UnixDomainSocketEndPoint(_socketPath));
            listener.Listen(backlog: 4);
            _listener = listener;
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static bool CanConnect(string socketPath)
    {
        try
        {
            using var probe = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            probe.Connect(new UnixDomainSocketEndPoint(socketPath));
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    public bool SignalExisting()
    {
        var socketPath = _socketPath ?? ResolveSocketPath();
        if (!File.Exists(socketPath))
            return false;

        try
        {
            using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            client.Connect(new UnixDomainSocketEndPoint(socketPath));
            // Connecting is itself the signal; a byte is sent so the owner's blocking
            // Accept/Receive completes deterministically rather than on socket teardown.
            client.Send([1]);
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    public void Listen(Action onShowRequested, Func<bool> stopWhen)
    {
        if (_listener is null)
            return;

        while (!stopWhen())
        {
            try
            {
                // Poll so shutdown is noticed even when no second instance ever launches.
                if (!_listener.Poll(TimeSpan.FromSeconds(1), SelectMode.SelectRead))
                    continue;

                using var connection = _listener.Accept();
                if (!stopWhen())
                    onShowRequested();
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException)
            {
                break;
            }
        }
    }

    public void Dispose()
    {
        // Only the process that bound the socket may remove the file. A failed TryAcquire
        // also records the path, and deleting it there would unbind the live owner.
        var owned = _listener is not null;
        _listener?.Dispose();
        _listener = null;

        if (owned && _socketPath is not null)
        {
            try { File.Delete(_socketPath); }
            catch (IOException) { /* another instance may have already replaced it */ }
        }
        _socketPath = null;
    }
}
