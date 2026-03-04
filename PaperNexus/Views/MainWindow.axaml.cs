using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using PaperNexus.Core;
using PaperNexus.ViewModels;
using System.Globalization;

namespace PaperNexus.Views;

public class FavoriteColorConverter : IValueConverter
{
    private static readonly IBrush ActiveBrush = new SolidColorBrush(Color.Parse("#E06C75"));
    private static readonly IBrush InactiveBrush = new SolidColorBrush(Color.Parse("#888888"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? ActiveBrush : InactiveBrush;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public partial class MainWindow : Window
{
    private int _versionClickCount;
    private DateTime _lastVersionClick = DateTime.MinValue;

    private static readonly string[] WallpaperCompliments =
    [
        "🎨 Excellent taste in wallpapers!",
        "🖼️ A true connoisseur of desktop aesthetics.",
        "✨ Your monitor is very lucky.",
        "🌅 You really know how to set the mood.",
        "🏆 Best wallpaper picker of the year award: you.",
        "👀 Someone's got an eye for beauty.",
        "🌟 This wallpaper? Chef's kiss.",
    ];

    private static readonly Key[] KonamiCode =
    [
        Key.Up, Key.Up, Key.Down, Key.Down,
        Key.Left, Key.Right, Key.Left, Key.Right,
        Key.B, Key.A,
    ];
    private int _konamiIndex;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new WallpaperConfigViewModel();
        UpdateButton.AddHandler(InputElement.PointerPressedEvent, OnUpdateButtonPointerPressed, RoutingStrategies.Tunnel);
    }

    private async void OnWallpaperNameDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not WallpaperConfigViewModel vm)
            return;
        var msg = WallpaperCompliments[Random.Shared.Next(WallpaperCompliments.Length)];
        await vm.ShowTransientStatusAsync(msg, 4000);
    }

    private async void OnVersionLabelPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var now = DateTime.UtcNow;
        if ((now - _lastVersionClick).TotalSeconds > 3)
            _versionClickCount = 0;
        _lastVersionClick = now;
        _versionClickCount++;

        if (_versionClickCount >= 5)
        {
            _versionClickCount = 0;
            if (DataContext is WallpaperConfigViewModel vm)
                await vm.ShowTransientStatusAsync("🥚 You found the easter egg! No wallpapers were harmed.", 5000);
        }
    }

    private void OnUpdateButtonPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            return;

        e.Handled = true;
        if (DataContext is WallpaperConfigViewModel vm)
            vm.CheckForUpdatesForceCommand.Execute(null);
    }

    protected override async void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == KonamiCode[_konamiIndex])
        {
            _konamiIndex++;
            if (_konamiIndex == KonamiCode.Length)
            {
                _konamiIndex = 0;
                if (DataContext is WallpaperConfigViewModel vm)
                    await vm.ShowTransientStatusAsync("🎮 +30 lives granted! (wallpaper edition)", 5000);
            }
        }
        else
        {
            _konamiIndex = e.Key == KonamiCode[0] ? 1 : 0;
        }
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        var settings = await WallpaperNexusSettings.LoadAsync();
        if (settings.WindowX.HasValue && settings.WindowY.HasValue)
            Position = new PixelPoint((int)settings.WindowX.Value, (int)settings.WindowY.Value);
        if (settings.WindowWidth.HasValue && settings.WindowHeight.HasValue)
        {
            Width = settings.WindowWidth.Value;
            Height = settings.WindowHeight.Value;
        }
        if (DataContext is WallpaperConfigViewModel vm)
            await vm.LoadAsync();
    }

    private async void AddSource_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var dialog = new WallpaperSourceDialog();
        var saved = await dialog.ShowDialog<bool>(this);
        if (saved && dialog.Result is not null && DataContext is WallpaperConfigViewModel vm)
        {
            vm.Sources.Add(dialog.Result);
            vm.SelectedSource = dialog.Result;
        }
    }

    private async void EditSource_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await OpenEditDialog();
    }

    private async void SourceList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        await OpenEditDialog();
    }

    private async Task OpenEditDialog()
    {
        if (DataContext is not WallpaperConfigViewModel vm || vm.SelectedSource is null)
            return;

        var dialog = new WallpaperSourceDialog(vm.SelectedSource);
        var saved = await dialog.ShowDialog<bool>(this);
        if (saved && dialog.Result is not null)
        {
            var index = vm.Sources.IndexOf(vm.SelectedSource);
            if (index < 0)
            {
                vm.Sources.Add(dialog.Result);
            }
            else
            {
                vm.Sources.RemoveAt(index);
                vm.Sources.Insert(index, dialog.Result);
            }
            vm.SelectedSource = dialog.Result;
        }
    }

    private async void DeleteWallpaper_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var cancelBtn = new Button { Content = "Cancel" };
        ToolTip.SetTip(cancelBtn, "Cancel deletion");
        var deleteBtn = new Button
        {
            Content = "Delete",
            Foreground = new SolidColorBrush(Color.Parse("#E06C75")),
        };
        ToolTip.SetTip(deleteBtn, "Permanently delete wallpaper file");
        var dialog = new Window
        {
            Title = "Delete Wallpaper",
            Width = 300,
            Height = 130,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.Parse("#2B2B2B")),
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 16,
                Children =
                {
                    new TextBlock { Text = "Delete this wallpaper file from disk?", TextWrapping = TextWrapping.Wrap },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children = { cancelBtn, deleteBtn },
                    },
                },
            },
        };
        cancelBtn.Click += (_, _) => dialog.Close(false);
        deleteBtn.Click += (_, _) => dialog.Close(true);
        var confirmed = await dialog.ShowDialog<bool>(this);
        if (confirmed && DataContext is WallpaperConfigViewModel vm)
            vm.DeleteCurrentWallpaperCommand.Execute(null);
    }

    private async void BrowseFolder_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            var result = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select Wallpapers Folder",
                AllowMultiple = false,
            });
            if (result.Count > 0 && DataContext is WallpaperConfigViewModel vm)
                vm.Folder = result[0].Path.LocalPath;
        }
        catch (Exception ex)
        {
            if (DataContext is WallpaperConfigViewModel vm)
                vm.StatusMessage = $"✗ Error browsing for folder: {ex.Message}";
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (Application.Current is App { IsExiting: false })
        {
            // Capture position before the window is destroyed
            _ = SaveWindowPositionAsync(Position.X, Position.Y, Width, Height);

            // Release ViewModel resources to reduce memory while running in tray
            if (DataContext is WallpaperConfigViewModel vm)
                vm.Cleanup();
            DataContext = null;
        }
        base.OnClosing(e);
    }

    private static readonly string[] DebugConfirmations =
    [
        "Are you sure you want to enable debug mode?",
        "Are you really sure?",
        "Debug mode is for developers. Are you a developer?",
        "Do you pinky promise you know what you're doing?",
        "Have you consulted your system administrator?",
        "Have you backed up your wallpapers? (You should.)",
        "On a scale of 1 to 10, is your certainty at least 7?",
        "Did you eat breakfast today? Mental clarity is important for decisions like this.",
        "Have you considered that your wallpapers might judge you for this?",
        "If a tree falls in a forest and no one hears it, should debug mode still be enabled?",
        "The last person who enabled debug mode was never seen again. Continue?",
        "We've notified your emergency contacts. Proceed anyway?",
        "This action may void your wallpaper warranty. Accept?",
        "NASA has been consulted and they recommend against it. Override NASA?",
        "The prophecy foretold of one who would enable debug mode. Is it you, The Chosen One?",
        "Final answer? No take-backs. This is legally binding in 47 countries.",
        "Okay fine. But we warned you. SEVENTEEN TIMES. Enable debug mode?",
    ];

    private async void DebugMode_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not WallpaperConfigViewModel vm)
            return;

        // Only intercept when toggling ON
        if (!DebugModeCheckBox.IsChecked.GetValueOrDefault())
            return;

        // Revert the check immediately — we'll set it if all 17 pass
        DebugModeCheckBox.IsChecked = false;
        vm.DebugMode = false;

        for (var i = 0; i < DebugConfirmations.Length; i++)
        {
            var confirmed = await ShowYesNoDialog(
                $"Confirmation {i + 1} of {DebugConfirmations.Length}",
                DebugConfirmations[i]);

            if (!confirmed)
            {
                var remaining = DebugConfirmations.Length - i;
                await vm.ShowTransientStatusAsync(
                    $"Debug mode cancelled. You only made it through {i} of {DebugConfirmations.Length} confirmations.", 5000);
                return;
            }
        }

        DebugModeCheckBox.IsChecked = true;
        vm.DebugMode = true;
        await vm.ShowTransientStatusAsync("Debug mode enabled. You absolute legend.", 5000);
    }

    private async void FactoryReset_Click(object? sender, RoutedEventArgs e)
    {
        var confirmed = await ShowYesNoDialog(
            "Factory Reset",
            "This will delete ALL application data (settings, logs, timers) and restart.\n\nYour downloaded wallpapers folder will NOT be deleted.\n\nAre you sure?");

        if (!confirmed)
            return;

        var doubleConfirmed = await ShowYesNoDialog(
            "Factory Reset — Final Confirmation",
            "This cannot be undone. Seriously. Last chance to back out.");

        if (!doubleConfirmed)
            return;

        try
        {
            var appDir = AppContext.BaseDirectory;
            var exePath = Environment.ProcessPath
                ?? Path.Combine(appDir, "PaperNexus.exe");

            // Delete everything in the app directory except the running exe
            foreach (var file in Directory.GetFiles(appDir))
            {
                if (string.Equals(Path.GetFullPath(file), Path.GetFullPath(exePath),
                    StringComparison.OrdinalIgnoreCase))
                    continue;
                try { File.Delete(file); }
                catch { /* skip locked files */ }
            }

            foreach (var dir in Directory.GetDirectories(appDir))
            {
                try { Directory.Delete(dir, recursive: true); }
                catch { /* skip locked directories */ }
            }

            // Restart the application
            Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            if (DataContext is WallpaperConfigViewModel vm)
                await vm.ShowTransientStatusAsync($"Factory reset failed: {ex.Message}", 5000);
        }
    }

    private async Task<bool> ShowYesNoDialog(string title, string message)
    {
        var noBtn = new Button { Content = "No" };
        ToolTip.SetTip(noBtn, "Cancel");
        var yesBtn = new Button { Content = "Yes" };
        ToolTip.SetTip(yesBtn, "Confirm");
        var dialog = new Window
        {
            Title = title,
            Width = 400,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.Parse("#2B2B2B")),
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 16,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children = { noBtn, yesBtn },
                    },
                },
            },
        };
        noBtn.Click += (_, _) => dialog.Close(false);
        yesBtn.Click += (_, _) => dialog.Close(true);
        return await dialog.ShowDialog<bool>(this);
    }

    private static async Task SaveWindowPositionAsync(int x, int y, double w, double h)
    {
        try
        {
            var settings = await WallpaperNexusSettings.LoadAsync();
            settings.WindowX = x;
            settings.WindowY = y;
            settings.WindowWidth = w;
            settings.WindowHeight = h;
            await settings.SaveAsync();
        }
        catch { }
    }
}
