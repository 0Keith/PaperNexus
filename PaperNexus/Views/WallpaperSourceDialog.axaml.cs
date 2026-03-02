using System.Text.RegularExpressions;
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
        TypeBox.SelectedIndex = 0;
        ImageUrlJPathBox.Text = "$[*].imageUrl";
        TitleJPathBox.Text = "$[*].title";
    }

    public WallpaperSourceDialog(WallpaperSource source) : this()
    {
        DialogTitle.Text = "Edit Wallpaper Source";
        NameBox.Text = source.Name;
        UrlBox.Text = source.Url;
        ImageUrlJPathBox.Text = source.ImageUrlJPath;
        TitleJPathBox.Text = source.TitleJPath;
        ImageUrlRegexBox.Text = source.ImageUrlRegex;
        CronBox.Text = source.CronExpression;
        EnabledBox.IsChecked = source.IsEnabled;
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        var name = NameBox.Text?.Trim() ?? string.Empty;
        var url = UrlBox.Text?.Trim() ?? string.Empty;
        var imageUrlJPath = ImageUrlJPathBox.Text?.Trim() ?? "$[*].imageUrl";
        var titleJPath = TitleJPathBox.Text?.Trim() ?? "$[*].title";
        var imageUrlRegex = ImageUrlRegexBox.Text?.Trim() ?? string.Empty;
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

        if (string.IsNullOrEmpty(imageUrlJPath))
        {
            ShowError("Image URL JPath is required.");
            return;
        }

        if (string.IsNullOrEmpty(titleJPath))
        {
            ShowError("Title JPath is required.");
            return;
        }

        if (!string.IsNullOrEmpty(imageUrlRegex))
        {
            try
            {
                _ = new Regex(imageUrlRegex);
            }
            catch (ArgumentException)
            {
                ShowError("Image URL Regex is not a valid regular expression.");
                return;
            }
        }

        if (string.IsNullOrEmpty(cron))
            cron = "0 * * * *";

        Result = new WallpaperSource
        {
            Name = name,
            Type = WallpaperSourceType.HttpJson,
            Url = url,
            ImageUrlJPath = imageUrlJPath,
            TitleJPath = titleJPath,
            ImageUrlRegex = imageUrlRegex,
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
