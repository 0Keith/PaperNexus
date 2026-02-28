using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Excogitated.WallpaperNexus.ViewModels;

namespace Excogitated.WallpaperNexus.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new WallpaperConfigViewModel();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (DataContext is WallpaperConfigViewModel vm)
            _ = vm.LoadAsync();
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
            Hide();
            return;
        }
        base.OnClosing(e);
    }
}
