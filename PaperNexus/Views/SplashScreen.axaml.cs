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
        StatusText.Text = EasterEggs.SplashMessage(Random.Shared);
    }
}
