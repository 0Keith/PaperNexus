using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using PaperNexus.Views;
using PaperNexus.ViewModels;
using Microsoft.Win32;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using Path = System.IO.Path;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace PaperNexus;

public partial class App : Application
{
    public static string AppVersion { get; } =
        Assembly.GetExecutingAssembly().GetName().Version is Version v
            ? $"v{v.Major}"
            : "v0";

    private IHost? _backgroundHost;
    private TrayIcon? _trayIcon;
    private MainWindow? _mainWindow;
    private SplashScreen? _splashScreen;
    private bool _exiting;

    internal bool IsExiting => _exiting;
    internal IServiceProvider? Services => _backgroundHost?.Services;
    private ILogger<App>? Logger => _backgroundHost?.Services.GetService<ILogger<App>>();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Keep running when any window is closed
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // In install mode, show only the install screen — skip all service/tray setup.
            if (Program.IsInstallMode)
            {
                new Views.InstallScreen().Show();
                base.OnFrameworkInitializationCompleted();
                return;
            }

            // Start background wallpaper services (download + switch)
            _backgroundHost = Host.CreateDefaultBuilder()
                .ConfigureServices(services =>
                {
                    services.AddLogging(b => b.AddProvider(new FileLoggerProvider()));
                    services.AddSingleton<HttpWallpaperSourceService>();
                    // Auto-discover and register all IAddSingleton / IAddHostedSingleton / IScheduleScopedJob implementations
                    services.AddServicesFrom(typeof(App).Assembly);
                })
                .Build();

            // Apply startup registration based on the persisted setting
#pragma warning disable CA1416
            _ = WallpaperNexusSettings.LoadAsync().ContinueWith(t =>
            {
                try { UpdateStartupRegistration(t.Result.RunOnStartup); }
                catch (Exception ex) { Logger?.LogError(ex, "Failed to apply startup registration on launch."); }
            });
#pragma warning restore CA1416

            var launchedOnStartup = desktop.Args?.Contains("--startup") == true;

            // Show splash screen while background services start (skip on startup and debug mode)
            if (!launchedOnStartup && !Program.IsDebugMode)
            {
                _splashScreen = new SplashScreen();
                _splashScreen.Show();
            }

            // Close the splash once the background host has started, but show it for at least 2 seconds.
            // In debug mode skip the delay and go straight to the main window — avoids a window-count-zero
            // gap that would trigger OnLastWindowClose shutdown before the main window opens.
            var splashDelay = Program.IsDebugMode ? Task.CompletedTask : Task.Delay(2000);
            _ = Task.WhenAll(_backgroundHost.StartAsync(), splashDelay).ContinueWith(_ =>
                Dispatcher.UIThread.Post(() =>
                {
                    _splashScreen?.Close();
                    _splashScreen = null;
                    if (!launchedOnStartup)
                        ShowMainWindow();
                }));

            // Show only the tray icon — no window at startup
            SetupTrayIcon(desktop);

            // Monitor for show-UI signals from second instances
            if (Program.ShowUIEvent is not null)
            {
                _ = Task.Run(() =>
                {
                    // Poll with a 1-second timeout so we can check _exiting without blocking forever
                    while (!_exiting)
                    {
                        try
                        {
                            if (Program.ShowUIEvent.WaitOne(1000))
                            {
                                if (!_exiting)
                                    ShowMainWindow();
                            }
                        }
                        catch (ObjectDisposedException)
                        {
                            // Event was disposed during shutdown — exit the listener loop
                            break;
                        }
                    }
                });
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    // Builds the system tray icon and context menu.
    // Each menu action runs switcher/downloader work on a background thread to avoid
    // blocking the UI thread, then surfaces errors through the settings window if it is open.
    private void SetupTrayIcon(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var menu = new NativeMenu();

        var openItem = new NativeMenuItem { Header = "Open Settings", Icon = CreateGearIcon() };
        openItem.Click += (_, _) => ShowMainWindow();
        menu.Items.Add(openItem);

        var nextItem = new NativeMenuItem { Header = "Next Wallpaper", Icon = CreatePlayIcon() };
        nextItem.Click += async (_, _) =>
        {
            try
            {
                var switcher = _backgroundHost?.Services.GetService<ISwitchWallpaper>();
                if (switcher is null)
                    return;
                var next = await Task.Run(switcher.SwitchToNextAsync);
                // If no wallpaper was found, trigger a fresh download then retry the switch
                if (next is null)
                {
                    var downloader = _backgroundHost?.Services.GetService<IDownloadWallpapers>();
                    if (downloader is not null)
                    {
                        await Task.Run(downloader.DownloadAllAsync);
                        next = await Task.Run(switcher.SwitchToNextAsync);
                    }
                }
                if (next is null)
                    Logger?.LogWarning("Tray 'Next Wallpaper' failed: no wallpapers available after download retry.");
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Error switching wallpaper from tray.");
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (_mainWindow?.DataContext is WallpaperConfigViewModel vm)
                        _ = vm.ShowTransientStatusAsync($"✗ Error switching wallpaper: {ex.Message}");
                });
            }
        };
        menu.Items.Add(nextItem);

        var randomItem = new NativeMenuItem { Header = "Random Wallpaper", Icon = CreateDiceIcon() };
        randomItem.Click += async (_, _) =>
        {
            try
            {
                var switcher = _backgroundHost?.Services.GetService<ISwitchWallpaper>();
                if (switcher is null)
                    return;
                var next = await Task.Run(switcher.SwitchToRandomAsync);
                // Same fallback pattern as "Next Wallpaper" — download if the folder is empty
                if (next is null)
                {
                    var downloader = _backgroundHost?.Services.GetService<IDownloadWallpapers>();
                    if (downloader is not null)
                    {
                        await Task.Run(downloader.DownloadAllAsync);
                        next = await Task.Run(switcher.SwitchToRandomAsync);
                    }
                }
                if (next is null)
                    Logger?.LogWarning("Tray 'Random Wallpaper' failed: no wallpapers available after download retry.");
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Error switching to random wallpaper from tray.");
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (_mainWindow?.DataContext is WallpaperConfigViewModel vm)
                        _ = vm.ShowTransientStatusAsync($"✗ Error switching wallpaper: {ex.Message}");
                });
            }
        };
        menu.Items.Add(randomItem);

        menu.Items.Add(new NativeMenuItemSeparator());

        var exitItem = new NativeMenuItem { Header = "Exit", Icon = CreatePowerIcon() };
        exitItem.Click += (_, _) => ExitApplication(desktop);
        menu.Items.Add(exitItem);

        _trayIcon = new TrayIcon
        {
            ToolTipText = "Paper Nexus",
            Icon = CreateTrayIcon(),
            Menu = menu,
        };
        _trayIcon.Clicked += (_, _) => ShowMainWindow();

        TrayIcon.SetIcons(this, [_trayIcon]);
    }

    // Ensures the settings window is created if needed, then brings it to the foreground.
    // Always called via Dispatcher.UIThread.Post to be safe from background threads.
    private void ShowMainWindow()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_mainWindow == null)
            {
                _mainWindow = new MainWindow();
                _mainWindow.Closed += OnMainWindowClosed;
            }
            else if (_mainWindow.DataContext is WallpaperConfigViewModel vm)
            {
                // Refresh preview in case the wallpaper changed while the window was not visible
                vm.RefreshPreviewImage();
            }
            _mainWindow.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
        });
    }

    // Called when the settings window is closed. Decides whether to stay resident in
    // the tray or to shut down, based on the MinimizeToTray setting and debug mode.
    private async void OnMainWindowClosed(object? sender, EventArgs e)
    {
        if (_mainWindow is not null)
        {
            _mainWindow.Closed -= OnMainWindowClosed;
            _mainWindow = null;
        }

        // Reclaim UI memory now that the settings window is closed
        GC.Collect(2, GCCollectionMode.Forced, blocking: false);

        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;

        // In debug mode, always exit on close
        if (Program.IsDebugMode)
        {
            ExitApplication(desktop);
            return;
        }

        // Check if minimize-to-tray is disabled; if so, exit on close
        try
        {
            var settings = await WallpaperNexusSettings.LoadAsync();
            if (!settings.MinimizeToTray)
                ExitApplication(desktop);
        }
        catch (Exception ex) { Logger?.LogWarning(ex, "Failed to load settings when checking MinimizeToTray; staying resident."); }
    }

    // Performs a graceful shutdown: hides the tray icon, stops background services
    // with a 3-second timeout, then forces process exit to clean up any stray threads.
    private async void ExitApplication(IClassicDesktopStyleApplicationLifetime desktop)
    {
        _exiting = true;
        // Hide the icon immediately so the user doesn't see a phantom tray entry
        if (_trayIcon != null)
            _trayIcon.IsVisible = false;
        if (_backgroundHost is not null)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try { await _backgroundHost.StopAsync(cts.Token); }
            catch (Exception ex) { Logger?.LogError(ex, "Error stopping background host during exit."); }
        }
        desktop.Shutdown();
        // Call Environment.Exit to ensure background threads (IPC listener, etc.) are terminated
        Environment.Exit(0);
    }

    // Loads the app logo asset and scales it to the standard 32×32 tray icon size.
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

    // Helper that renders a 16×16 menu icon using a SixLabors drawing callback,
    // then converts it to an Avalonia Bitmap via an in-memory PNG stream.
    private static Avalonia.Media.Imaging.Bitmap CreateMenuIcon(Action<IImageProcessingContext> draw)
    {
        using var img = new Image<Rgba32>(16, 16);
        img.Mutate(draw);
        using var ms = new MemoryStream();
        img.SaveAsPng(ms);
        ms.Position = 0;
        return new Avalonia.Media.Imaging.Bitmap(ms);
    }

    private static Avalonia.Media.Imaging.Bitmap CreateGearIcon() => CreateMenuIcon(ctx =>
    {
        ctx.Fill(Color.CornflowerBlue, new Star(new PointF(8, 8), 8, 3.5f, 7f));
        ctx.Fill(Color.White, new EllipsePolygon(new PointF(8, 8), 2.5f));
    });

    private static Avalonia.Media.Imaging.Bitmap CreatePlayIcon() => CreateMenuIcon(ctx =>
    {
        ctx.Fill(Color.LimeGreen, new Polygon(
            new LinearLineSegment(new PointF(4, 2), new PointF(14, 8), new PointF(4, 14))));
    });

    private static Avalonia.Media.Imaging.Bitmap CreateDiceIcon() => CreateMenuIcon(ctx =>
    {
        ctx.Fill(Color.MediumOrchid, new RectangularPolygon(2, 2, 12, 12));
        ctx.Fill(Color.White, new EllipsePolygon(new PointF(5, 5), 1.3f));
        ctx.Fill(Color.White, new EllipsePolygon(new PointF(8, 8), 1.3f));
        ctx.Fill(Color.White, new EllipsePolygon(new PointF(11, 11), 1.3f));
    });

    private static Avalonia.Media.Imaging.Bitmap CreatePowerIcon() => CreateMenuIcon(ctx =>
    {
        ctx.Draw(Color.Tomato, 2f, new EllipsePolygon(new PointF(8, 9), 5));
        ctx.Fill(Color.Tomato, new RectangularPolygon(7, 2, 2, 7));
    });

    // Adds or removes the Windows startup registry entry under HKCU\...\Run.
    // Also removes legacy key names from earlier app versions to clean up on upgrade.
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    internal static void UpdateStartupRegistration(bool enable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: true);
        // Clean up old names from previous app identity
        key?.DeleteValue("Excogitated Wallpaper Service", throwOnMissingValue: false);
        key?.DeleteValue("Wallpaper Nexus", throwOnMissingValue: false);
        if (enable)
        {
            var exePath = Environment.ProcessPath
                ?? Path.ChangeExtension(Assembly.GetEntryAssembly()!.Location, ".exe");
            // Pass --startup so the app knows it was launched by Windows at login
            key?.SetValue("PaperNexus", $"\"{exePath}\" --startup");
        }
        else
        {
            key?.DeleteValue("PaperNexus", throwOnMissingValue: false);
        }
    }
}
