using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using PaperNexus.Views;
using Microsoft.Win32;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace PaperNexus;

public partial class App : Application
{
    internal static string AppVersion { get; } =
        Assembly.GetExecutingAssembly().GetName().Version is { } v
            ? $"v{v.Major}.{v.Minor}.{v.Build}"
            : "v0.0.0";

    private IHost? _backgroundHost;
    private TrayIcon? _trayIcon;
    private MainWindow? _mainWindow;
    private SplashScreen? _splashScreen;
    private bool _exiting;

    internal bool IsExiting => _exiting;
    internal IServiceProvider? Services => _backgroundHost?.Services;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Keep running when the settings window is closed
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Start background wallpaper services (download + switch)
            _backgroundHost = Host.CreateDefaultBuilder()
                .ConfigureServices(services =>
                {
                    services.AddLogging(b => b.AddProvider(new FileLoggerProvider()));
                    services.AddSingleton<HttpWallpaperSourceService>();
                    services.AddHostedService<DownloadWallpapers>();
                    services.AddServicesFrom(typeof(App).Assembly);
                })
                .Build();

            // Register in Windows startup so the app launches with the OS
#pragma warning disable CA1416
            RegisterStartup();
#pragma warning restore CA1416

            // Show splash screen while background services start
            _splashScreen = new SplashScreen();
            _splashScreen.Show();

            // Close the splash once the background host has started, but show it for at least 2 seconds
            _ = Task.WhenAll(_backgroundHost.StartAsync(), Task.Delay(2000)).ContinueWith(_ =>
                Dispatcher.UIThread.Post(() =>
                {
                    _splashScreen?.Close();
                    _splashScreen = null;
                }));

            // Show only the tray icon — no window at startup
            SetupTrayIcon(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void SetupTrayIcon(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var menu = new NativeMenu();

        var openItem = new NativeMenuItem { Header = "Open Settings" };
        openItem.Click += (_, _) => ShowMainWindow();
        menu.Items.Add(openItem);

        var nextItem = new NativeMenuItem { Header = "Next Wallpaper" };
        nextItem.Click += async (_, _) =>
        {
            try
            {
                var switcher = _backgroundHost?.Services.GetService<ISwitchWallpaper>();
                if (switcher is not null)
                    await Task.Run(switcher.SwitchToNextAsync);
            }
            catch { /* Non-critical: wallpaper switch may fail silently */ }
        };
        menu.Items.Add(nextItem);

        menu.Items.Add(new NativeMenuItemSeparator());

        var exitItem = new NativeMenuItem { Header = "Exit" };
        exitItem.Click += (_, _) => ExitApplication(desktop);
        menu.Items.Add(exitItem);

        _trayIcon = new TrayIcon
        {
            ToolTipText = "Wallpaper Nexus",
            Icon = CreateTrayIcon(),
            Menu = menu,
        };
        _trayIcon.Clicked += (_, _) => ShowMainWindow();

        TrayIcon.SetIcons(this, new TrayIcons { _trayIcon });
    }

    private void ShowMainWindow()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_mainWindow == null)
                _mainWindow = new MainWindow();
            _mainWindow.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
        });
    }

    private async void ExitApplication(IClassicDesktopStyleApplicationLifetime desktop)
    {
        _exiting = true;
        if (_trayIcon != null)
            _trayIcon.IsVisible = false;
        if (_backgroundHost is not null)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try { await _backgroundHost.StopAsync(cts.Token); }
            catch { }
        }
        desktop.Shutdown();
        Environment.Exit(0);
    }

    private static WindowIcon CreateTrayIcon()
    {
        using var stream = AssetLoader.Open(new Uri("avares://PaperNexus/Assets/logo.png"));
        using var image = SixLabors.ImageSharp.Image.Load(stream);
        image.Mutate(ctx => ctx.Resize(32, 32));
        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        ms.Position = 0;
        return new WindowIcon(new Avalonia.Media.Imaging.Bitmap(ms));
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void RegisterStartup()
    {
        try
        {
            var exePath = Environment.ProcessPath
                ?? Path.ChangeExtension(Assembly.GetEntryAssembly()!.Location, ".exe");
            using var key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: true);
            key?.DeleteValue("Excogitated Wallpaper Service", throwOnMissingValue: false);
            key?.DeleteValue("Wallpaper Nexus", throwOnMissingValue: false);
            key?.SetValue("PaperNexus", $"\"{exePath}\"");
        }
        catch { /* Non-critical: startup registration may fail on restricted machines */ }
    }
}
