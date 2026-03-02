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
        CronBox.Text = source.CronExpression;
        EnabledBox.IsChecked = source.IsEnabled;
    }

    private async void Test_Click(object? sender, RoutedEventArgs e)
    {
        HideMessages();
        var url = UrlBox.Text?.Trim() ?? string.Empty;
        var imageUrlJPath = ImageUrlJPathBox.Text?.Trim() ?? "$[*].imageUrl";
        var titleJPath = TitleJPathBox.Text?.Trim() ?? "$[*].title";

        if (string.IsNullOrEmpty(url))
        {
            ShowError("URL is required to test the source.");
            return;
        }

        TestButtonText.Text = "Testing…";
        try
        {
            var service = new HttpWallpaperSourceService(Microsoft.Extensions.Logging.Abstractions.NullLogger<HttpWallpaperSourceService>.Instance);
            var source = new WallpaperSource
            {
                Name = NameBox.Text?.Trim() ?? string.Empty,
                Url = url,
                ImageUrlJPath = imageUrlJPath,
                TitleJPath = titleJPath,
            };
            var images = await service.GetImages(source);
            var preview = images.Take(5).Select((img, i) =>
                $"{i + 1}. {img.Title}\n   {img.ImageUrl}");
            ShowTestResult($"Success — {images.Count} image(s) found.\n\n{string.Join("\n\n", preview)}");
        }
        catch (Exception ex)
        {
            ShowError($"Test failed: {ex.Message}");
        }
        finally
        {
            TestButtonText.Text = "Test";
        }
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        var name = NameBox.Text?.Trim() ?? string.Empty;
        var url = UrlBox.Text?.Trim() ?? string.Empty;
        var imageUrlJPath = ImageUrlJPathBox.Text?.Trim() ?? "$[*].imageUrl";
        var titleJPath = TitleJPathBox.Text?.Trim() ?? "$[*].title";
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

        if (string.IsNullOrEmpty(cron))
            cron = "0 * * * *";

        Result = new WallpaperSource
        {
            Name = name,
            Type = WallpaperSourceType.HttpJson,
            Url = url,
            ImageUrlJPath = imageUrlJPath,
            TitleJPath = titleJPath,
            CronExpression = cron,
            IsEnabled = EnabledBox.IsChecked ?? true,
        };
        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private void HideMessages()
    {
        ErrorText.IsVisible = false;
        TestResultText.IsVisible = false;
    }

    private void ShowError(string message)
    {
        TestResultText.IsVisible = false;
        ErrorText.Text = message;
        ErrorText.IsVisible = true;
    }

    private void ShowTestResult(string message)
    {
        ErrorText.IsVisible = false;
        TestResultText.Text = message;
        TestResultText.IsVisible = true;
    }
}
