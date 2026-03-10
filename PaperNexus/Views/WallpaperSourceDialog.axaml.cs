using Avalonia.Controls;
using Avalonia.Interactivity;
using PaperNexus.Core;

namespace PaperNexus.Views;

public partial class WallpaperSourceDialog : Window
{
    public WallpaperSource? Result { get; private set; }

    // Tracks any in-flight Test request so it can be cancelled if the user clicks
    // Test again or closes the dialog before the previous request completes.
    private CancellationTokenSource? _testCts;

    public WallpaperSourceDialog()
    {
        Opacity = 0;
        InitializeComponent();
        TypeBox.SelectedIndex = 0;
        ImageUrlJPathBox.Text = "$[*].imageUrl";
        TitleJPathBox.Text = "$[*].title";
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        Opacity = 1;
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

        if (!ValidateHttpsUrl(url))
            return;

        // Cancel any previous in-flight test before starting a new one.
        // This prevents stale responses from arriving out of order if the user
        // edits the URL and clicks Test again before the first request finishes.
        var oldCts = _testCts;
        oldCts?.Cancel();
        oldCts?.Dispose();
        _testCts = new CancellationTokenSource();
        var ct = _testCts.Token;

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
            var images = await service.GetImagesAsync(source, ct);
            var preview = images.Select(img =>
                $"Title: {img.Title}\nImage: {img.ImageUrl}");
            ShowTestResult($"Success — {images.Count} image(s) found.\n\n{string.Join("\n\n", preview)}");
        }
        catch (OperationCanceledException)
        {
            // Test was superseded by a new request — silently discard the result
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

        if (!ValidateHttpsUrl(url))
            return;

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

        try
        {
            Cronos.CronExpression.Parse(cron);
        }
        catch (Cronos.CronFormatException)
        {
            ShowError("Invalid cron expression.");
            return;
        }

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

    // Cancel and dispose any in-flight test request when the dialog is closed,
    // regardless of whether it was dismissed via Save, Cancel, or the title-bar X.
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        var cts = _testCts;
        _testCts = null;
        cts?.Cancel();
        cts?.Dispose();
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

    private bool ValidateHttpsUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) || parsed.Scheme != Uri.UriSchemeHttps)
        {
            ShowError("URL must use HTTPS.");
            return false;
        }
        return true;
    }
}
