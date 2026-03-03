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

    [STAThread]
    public static void Main(string[] args)
    {
        var installDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PaperNexus");
        var installPath = Path.Combine(installDir, "PaperNexus.exe");

        var currentPath = Environment.ProcessPath
            ?? Path.Combine(AppContext.BaseDirectory, "PaperNexus.exe");

        var isInstalled = string.Equals(
            Path.GetFullPath(currentPath),
            Path.GetFullPath(installPath),
            StringComparison.OrdinalIgnoreCase);

        if (!isInstalled)
        {
            // Not running from the install location.
            // If an installed instance is already running, signal it to show UI.
#pragma warning disable CA1416
            if (EventWaitHandle.TryOpenExisting(EventName, out var existingEvent))
            {
                existingEvent.Set();
                existingEvent.Dispose();
                return;
            }

            // No running instance. Install (copy exe) and launch from install location.
            try
            {
                Directory.CreateDirectory(installDir);
                File.Copy(currentPath, installPath, overwrite: true);
                MigrateFileIfNeeded("settings.json", currentPath, installDir);
                MigrateFileIfNeeded("timers.json", currentPath, installDir);
            }
            catch (IOException)
            {
                // File may be locked by a running instance we could not detect.
                // If the installed copy already exists, launch it anyway.
                if (!File.Exists(installPath))
                    return;
            }

            Process.Start(new ProcessStartInfo(installPath) { UseShellExecute = true });
#pragma warning restore CA1416
            return;
        }

        // Running from the install location — proceed as the primary instance.

        // Enforce single instance so concurrent update batch scripts cannot spawn
        // multiple copies that all download and re-launch, creating an infinite loop.
        using var mutex = new Mutex(false, MutexName);
        bool acquired;
        try
        {
            acquired = mutex.WaitOne(0, exitContext: false);
        }
        catch (AbandonedMutexException)
        {
            acquired = true; // previous instance crashed; we now own the mutex
        }

        if (!acquired)
        {
            // Another installed instance is running — signal it to show UI.
#pragma warning disable CA1416
            if (EventWaitHandle.TryOpenExisting(EventName, out var existingEvent))
            {
                existingEvent.Set();
                existingEvent.Dispose();
            }
#pragma warning restore CA1416
            return;
        }

        // Create the IPC event handle for show-UI signals from other instances.
        // AutoReset: each Set() unblocks exactly one WaitOne().
        ShowUIEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);

        using var loggerProvider = new FileLoggerProvider();
        var logger = loggerProvider.CreateLogger(nameof(Program));

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                logger.LogCritical(ex, "Unhandled exception");
            else
                logger.LogCritical("Unhandled non-exception: {ExceptionObject}", e.ExceptionObject);
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            logger.LogCritical(e.Exception, "Unobserved task exception");
            e.SetObserved();
        };

        try
        {
            AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace()
                .StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            ShowUIEvent.Dispose();
            ShowUIEvent = null;
        }
    }

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
