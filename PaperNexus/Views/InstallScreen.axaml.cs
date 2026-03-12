using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Microsoft.Win32;
using PaperNexus.Core;

namespace PaperNexus.Views;

public partial class InstallScreen : Window
{
    public InstallScreen()
    {
        InitializeComponent();
        InstallPathText.Text = Program.InstallDir;
    }

    private async void OnBrowseClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var results = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Choose install folder",
                AllowMultiple = false,
            });

            if (results is [var folder])
            {
                var selected = folder.Path.LocalPath;
                // Always install into a "PaperNexus" subfolder of whatever the user picks.
                InstallPathText.Text = Path.Combine(selected, "PaperNexus");
            }
        }
        catch { /* picker cancelled or unavailable — leave path unchanged */ }
    }

    private async void OnInstallClicked(object sender, RoutedEventArgs e)
    {
        // Prevent double-click and give visual feedback while copying.
        InstallButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
        BrowseButton.IsEnabled = false;
        InstallButton.Content = "Installing\u2026";

        // Fall back to the default AppData path if the user cleared the field.
        var installDir = InstallPathText.Text?.Trim();
        if (string.IsNullOrEmpty(installDir))
            installDir = Program.InstallDir;
        var installExePath = Path.Combine(installDir, "PaperNexus.exe");

        try
        {
            // Run the file copy off the UI thread to avoid blocking the window.
            var success = await Task.Run(() =>
                Program.TryInstall(Program.CurrentExePath, installDir, installExePath));

            if (!success)
            {
                // Install failed — re-enable so the user can try again.
                InstallButton.Content = "Install";
                InstallButton.IsEnabled = true;
                CancelButton.IsEnabled = true;
                BrowseButton.IsEnabled = true;
                return;
            }

            // Persist the startup preference to AppData so the installed app finds it on first launch.
            // Settings always live at WallpaperNexusSettings.SettingsFilePath regardless of exe location.
            // Only write fresh settings when none were migrated from alongside the source exe.
            if (!File.Exists(WallpaperNexusSettings.SettingsFilePath))
            {
                var settings = new WallpaperNexusSettings
                {
                    RunOnStartup = RunOnStartupCheckBox.IsChecked == true,
                };
                await settings.SaveAsync();
            }

            // Always touch the registry during install so a stale entry from a previous
            // install at a different path doesn't survive and launch the wrong (possibly
            // missing) exe on Windows startup.
            // (App.UpdateStartupRegistration uses Environment.ProcessPath which is the
            // un-installed copy, so we must write the registry entry here ourselves.)
#pragma warning disable CA1416
            using var key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (RunOnStartupCheckBox.IsChecked == true)
                key?.SetValue("PaperNexus", $"\"{installExePath}\" --startup");
            else
                key?.DeleteValue("PaperNexus", throwOnMissingValue: false);
#pragma warning restore CA1416

            // Launch the newly-installed copy and exit the installer process.
            Process.Start(new ProcessStartInfo(installExePath) { UseShellExecute = true });
            Environment.Exit(0);
        }
        catch
        {
            // Restore interactive state so the user can retry or cancel.
            InstallButton.Content = "Install";
            InstallButton.IsEnabled = true;
            CancelButton.IsEnabled = true;
            BrowseButton.IsEnabled = true;
        }
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        Environment.Exit(0);
    }
}
