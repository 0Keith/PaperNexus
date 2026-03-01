using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using PaperNexus.Core;
using PaperNexus.ViewModels;

namespace PaperNexus.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new WallpaperConfigViewModel();
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
                vm.WallpapersFolder = result[0].Path.LocalPath;
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
