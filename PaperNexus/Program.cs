global using PaperNexus.Core;
global using PaperNexus;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Hosting;
global using Microsoft.Extensions.Logging;
global using Newtonsoft.Json;
global using System.Diagnostics;
global using System.Reflection;
global using Avalonia;

internal sealed class Program
{
    private const string EventName = "PaperNexus_ShowUI";
    private const string MutexName = "PaperNexus_SingleInstance";

    internal static EventWaitHandle? ShowUIEvent { get; private set; }
    internal static bool IsDebugMode { get; private set; }
    internal static bool IsInstallMode { get; private set; }

    // Install path data exposed for InstallScreen to use when performing the actual install.
    internal static string CurrentExePath { get; private set; } = string.Empty;
    internal static string InstallDir { get; private set; } = string.Empty;
    internal static string InstallExePath { get; private set; } = string.Empty;

    [STAThread]
    public static void Main(string[] args)
    {
        // Debug mode bypasses the install check and single-instance logic entirely.
        if (TryRunDebugMode(args))
            return;

        var (installDir, installPath) = GetInstallPaths();
        var currentPath = GetCurrentProcessPath();

        // Cache paths so InstallScreen can reference them without recomputing.
        InstallDir = installDir;
        InstallExePath = installPath;
        CurrentExePath = currentPath;

        // If running from a location other than the install directory, perform
        // first-run installation and then hand off to the installed copy.
        if (!IsRunningFromInstallLocation(currentPath, installPath))
        {
            HandleNotInstalledLaunch(args);
            return;
        }

        RunAsPrimaryInstance(args);
    }

    private static bool TryRunDebugMode(string[] args)
    {
        if (!args.Contains("--debug"))
            return false;
        IsDebugMode = true;
        RunApp(args);
        return true;
    }

    private static (string installDir, string installPath) GetInstallPaths()
    {
        var path1 = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var installDir = Path.Combine(path1, "PaperNexus");
        return (installDir, Path.Combine(installDir, "PaperNexus.exe"));
    }

    private static string GetCurrentProcessPath()
    {
        return Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "PaperNexus.exe");
    }

    private static bool IsRunningFromInstallLocation(string currentPath, string installPath)
    {
        var current = Path.GetFullPath(currentPath);
        var install = Path.GetFullPath(installPath);
        return string.Equals(current, install, StringComparison.OrdinalIgnoreCase);
    }

    // Handles startup when the exe is not yet at the install location.
    // Signals any already-running instance first; if none is running, shows the install screen.
    private static void HandleNotInstalledLaunch(string[] args)
    {
        // If a running instance exists, signal it to show its UI and exit without installing.
        if (TrySignalExistingInstance())
            return;

        // Show the install screen — it performs the actual copy and relaunch when confirmed.
        IsInstallMode = true;
        RunApp(args);
    }

    // Copies the exe to the install directory and migrates any adjacent data files.
    // Returns true if install succeeded or the installed copy already exists (race with another instance).
    internal static bool TryInstall(string currentPath, string installDir, string installPath)
    {
        try
        {
            Directory.CreateDirectory(installDir);
            File.Copy(currentPath, installPath, overwrite: true);
            // Carry over persisted data from beside the downloaded exe, if present.
            MigrateFileIfNeeded("settings.json", currentPath, installDir);
            MigrateFileIfNeeded("timers.json", currentPath, installDir);
            return true;
        }
        catch (IOException)
        {
            // File may be locked by a running instance we could not detect.
            // Launch the existing copy if it's already there.
            return File.Exists(installPath);
        }
    }

    // Enforces single-instance semantics: acquires the named mutex or signals the
    // already-running instance to show its window, then runs the Avalonia app loop.
    private static void RunAsPrimaryInstance(string[] args)
    {
        using var mutex = new Mutex(false, MutexName);
        // If we can't own the mutex, another instance is already running.
        if (!TryAcquireMutex(mutex))
        {
            TrySignalExistingInstance();
            return;
        }

        // Create the IPC event handle for show-UI signals from other instances.
        // AutoReset: each Set() unblocks exactly one WaitOne().
        ShowUIEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
        RunApp(args);
        ShowUIEvent.Dispose();
        ShowUIEvent = null;
    }

    private static bool TryAcquireMutex(Mutex mutex)
    {
        try
        {
            return mutex.WaitOne(0, exitContext: false);
        }
        catch (AbandonedMutexException)
        {
            return true; // previous instance crashed; we now own the mutex
        }
    }

    private static bool TrySignalExistingInstance()
    {
#pragma warning disable CA1416
        if (!EventWaitHandle.TryOpenExisting(EventName, out var existingEvent))
            return false;
#pragma warning restore CA1416
        using (existingEvent)
            existingEvent.Set();
        return true;
    }

    // Wires up global exception logging, then starts the Avalonia application loop.
    // The FileLoggerProvider is kept alive for the duration of the process so all
    // log messages are flushed before the process exits.
    private static void RunApp(string[] args)
    {
        using var loggerProvider = new FileLoggerProvider();
        var logger = loggerProvider.CreateLogger(nameof(Program));

        // Catch exceptions that escape all managed thread roots (non-UI threads, finalizers, etc.)
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                logger.LogCritical(ex, "Unhandled exception");
            else
                logger.LogCritical("Unhandled non-exception: {ExceptionObject}", e.ExceptionObject);
        };

        // Prevent fire-and-forget tasks from silently swallowing exceptions
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            logger.LogCritical(e.Exception, "Unobserved task exception");
            // Mark as observed so the process does not crash on GC finalisation
            e.SetObserved();
        };

        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .StartWithClassicDesktopLifetime(args);
    }

    // Copies a data file from alongside the source exe to the install directory,
    // but only if it doesn't already exist at the destination (never overwrites).
    private static void MigrateFileIfNeeded(string fileName, string sourceExePath, string installDir)
    {
        var sourceDir = Path.GetDirectoryName(sourceExePath);
        if (sourceDir is null)
            return;
        var source = Path.Combine(sourceDir, fileName);
        var dest = Path.Combine(installDir, fileName);
        if (File.Exists(source) && !File.Exists(dest))
        {
            try { File.Copy(source, dest); }
            catch { /* best-effort migration */ }
        }
    }
}
