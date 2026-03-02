using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using PaperNexus.Core;
using PaperNexus.ViewModels;

namespace PaperNexus.Views;

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
            vm.Sources.RemoveAt(index);
            vm.Sources.Insert(index, dialog.Result);
            vm.SelectedSource = dialog.Result;
        }
    }

    private async void DeleteWallpaper_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var cancelBtn = new Button { Content = "Cancel" };
        var deleteBtn = new Button
        {
            Content = "Delete",
            Foreground = new SolidColorBrush(Color.Parse("#E06C75")),
        };
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
        // Hide to tray instead of closing, unless the app is actually exiting
        if (Application.Current is App { IsExiting: false })
        {
            e.Cancel = true;
            _ = SaveWindowPositionAsync();
            Hide();
            return;
        }
        base.OnClosing(e);
    }

    private async Task SaveWindowPositionAsync()
    {
        try
        {
            var settings = await WallpaperNexusSettings.LoadAsync();
            settings.WindowX = Position.X;
            settings.WindowY = Position.Y;
            settings.WindowWidth = Width;
            settings.WindowHeight = Height;
            await settings.SaveAsync();
        }
        catch { }
    }
}
