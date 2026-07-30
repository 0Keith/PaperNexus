using System.Diagnostics;

namespace PaperNexus.Core.Platform;

// Opens folders, files, and URLs in the user's default handler.
public static class ShellOpener
{
    // UseShellExecute routes through the Windows shell and through xdg-open on Linux,
    // so one call covers both - but only when a desktop portal is present.
    public static void Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception) when (OperatingSystem.IsLinux())
        {
            // .NET's shell-execute fallback is not available in every Linux session;
            // invoking xdg-open directly still works wherever xdg-utils is installed.
            LinuxDesktop.StartDetached("xdg-open", target);
        }
        catch (Exception)
        {
            // No handler registered - nothing useful to do beyond not crashing the caller.
        }
    }

    // Reveals a folder in the platform file manager.
    public static void OpenFolder(string folderPath)
    {
        if (OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo("explorer.exe", folderPath));
            return;
        }
        Open(folderPath);
    }

    // Launches another copy of the app (used by the installer hand-off and factory reset).
    public static void LaunchExecutable(string exePath)
    {
        PlatformPaths.EnsureExecutable(exePath);
        Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
    }
}
