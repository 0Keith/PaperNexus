using Avalonia.Controls;
using PaperNexus.Core;

namespace PaperNexus.Views;

public partial class SplashScreen : Window
{
    public SplashScreen()
    {
        InitializeComponent();
        VersionText.Text = App.AppVersion;
        // Usually the ordinary "Starting up..." line; occasionally something else, so it
        // reads as a surprise rather than a gimmick that wears out by the third launch.
        var splashLine = EasterEggs.SplashMessage(Random.Shared);
        StatusText.Text = splashLine;
        // This egg has no overlay to record it, so it is recorded where it is shown.
        if (splashLine != EasterEggs.DefaultSplashMessage)
            _ = EasterEggProgress.RecordAsync(EasterEggCatalog.Splash);
    }
}
