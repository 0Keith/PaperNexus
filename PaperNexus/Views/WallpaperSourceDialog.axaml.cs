using Avalonia.Controls;
using Avalonia.Interactivity;
using PaperNexus.Core;

namespace PaperNexus.Views;

public partial class WallpaperSourceDialog : Window
{
    public WallpaperSource? Result { get; private set; }

    public WallpaperSourceDialog()
    {
        InitializeComponent();
    }

    public WallpaperSourceDialog(WallpaperSource source) : this()
    {
        DialogTitle.Text = "Edit Wallpaper Source";
        NameBox.Text = source.Name;
        UrlBox.Text = source.Url;
        CronBox.Text = source.CronExpression;
        EnabledBox.IsChecked = source.IsEnabled;
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        var name = NameBox.Text?.Trim() ?? string.Empty;
        var url = UrlBox.Text?.Trim() ?? string.Empty;
        var cron = CronBox.Text?.Trim();

        if (string.IsNullOrEmpty(name))
        {
            ShowError("Name is required.");
            return;
        }

        if (string.IsNullOrEmpty(url))
        {
            ShowError("URL is required.");
            return;
        }

        if (string.IsNullOrEmpty(cron))
            cron = "0 * * * *";

        Result = new WallpaperSource
        {
            Name = name,
            Url = url,
            CronExpression = cron,
            IsEnabled = EnabledBox.IsChecked ?? true,
        };
        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.IsVisible = true;
    }
}
