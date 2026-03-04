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
            // Keep running when the settings window is closed
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Start background wallpaper services (download + switch)
            _backgroundHost = Host.CreateDefaultBuilder()
                .ConfigureServices(services =>
                {
                    services.AddLogging(b => b.AddProvider(new FileLoggerProvider()));
                    services.AddSingleton<HttpWallpaperSourceService>();
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

            // Show splash screen while background services start (skip on startup)
            if (!launchedOnStartup)
            {
                _splashScreen = new SplashScreen();
                _splashScreen.Show();
            }

            // Close the splash once the background host has started, but show it for at least 2 seconds
            _ = Task.WhenAll(_backgroundHost.StartAsync(), Task.Delay(2000)).ContinueWith(_ =>
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
                            break;
                        }
                    }
                });
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

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
                if (next is null)
                {
                    var downloader = _backgroundHost?.Services.GetService<IDownloadWallpapers>();
                    if (downloader is not null)
                    {
                        await Task.Run(downloader.DownloadAllAsync);
                        await Task.Run(switcher.SwitchToNextAsync);
                    }
                }
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
                if (next is null)
                {
                    var downloader = _backgroundHost?.Services.GetService<IDownloadWallpapers>();
                    if (downloader is not null)
                    {
                        await Task.Run(downloader.DownloadAllAsync);
                        await Task.Run(switcher.SwitchToRandomAsync);
                    }
                }
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

        TrayIcon.SetIcons(this, new TrayIcons { _trayIcon });
    }

    private void ShowMainWindow()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_mainWindow == null)
            {
                _mainWindow = new MainWindow();
                _mainWindow.Closed += OnMainWindowClosed;
            }
            _mainWindow.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
        });
    }

    private void OnMainWindowClosed(object? sender, EventArgs e)
    {
        if (_mainWindow is not null)
        {
            _mainWindow.Closed -= OnMainWindowClosed;
            _mainWindow = null;
        }

        // Reclaim UI memory now that the settings window is closed
        GC.Collect(2, GCCollectionMode.Forced, blocking: false);
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
            catch (Exception ex) { Logger?.LogError(ex, "Error stopping background host during exit."); }
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

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    internal static void UpdateStartupRegistration(bool enable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: true);
        key?.DeleteValue("Excogitated Wallpaper Service", throwOnMissingValue: false);
        key?.DeleteValue("Wallpaper Nexus", throwOnMissingValue: false);
        if (enable)
        {
            var exePath = Environment.ProcessPath
                ?? Path.ChangeExtension(Assembly.GetEntryAssembly()!.Location, ".exe");
            key?.SetValue("PaperNexus", $"\"{exePath}\" --startup");
        }
        else
        {
            key?.DeleteValue("PaperNexus", throwOnMissingValue: false);
        }
    }
}
