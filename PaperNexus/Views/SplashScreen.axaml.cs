using Avalonia.Controls;

namespace PaperNexus.Views;

public partial class SplashScreen : Window
{
    public SplashScreen()
    {
        InitializeComponent();
        VersionText.Text = App.AppVersion;
    }
}
