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
    [STAThread]
    public static void Main(string[] args)
    {
        // Enforce single instance so concurrent update batch scripts cannot spawn
        // multiple copies that all download and re-launch, creating an infinite loop.
        using var mutex = new Mutex(false, "PaperNexus_SingleInstance");
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
            return;

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

        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .StartWithClassicDesktopLifetime(args);
    }
}

