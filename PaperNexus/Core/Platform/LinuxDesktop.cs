using System.Diagnostics;

namespace PaperNexus.Core.Platform;

// The Linux desktop environments PaperNexus knows how to drive. Anything unrecognised
// falls back to a generic X11/Wayland setter tool if one is installed.
public enum LinuxDesktopEnvironment
{
    Unknown,
    Kde,
    Gnome,
}

// Detects the running desktop environment and runs short-lived helper commands.
// Kept separate from the wallpaper logic so tests can exercise the command-building
// without spawning real processes.
public static class LinuxDesktop
{
    // Reads the freedesktop-standard environment variables the session manager sets.
    // XDG_CURRENT_DESKTOP is colon-separated (e.g. "ubuntu:GNOME"), so match on substrings.
    public static LinuxDesktopEnvironment Detect()
    {
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP"),
            Environment.GetEnvironmentVariable("XDG_SESSION_DESKTOP"),
            Environment.GetEnvironmentVariable("DESKTOP_SESSION"),
        };

        var fromEnvironment = Classify(candidates);
        if (fromEnvironment != LinuxDesktopEnvironment.Unknown)
            return fromEnvironment;

        // No desktop hint in the environment - infer from what is actually running.
        if (IsProcessRunning("plasmashell"))
            return LinuxDesktopEnvironment.Kde;
        if (IsProcessRunning("gnome-shell"))
            return LinuxDesktopEnvironment.Gnome;

        return LinuxDesktopEnvironment.Unknown;
    }

    // Maps freedesktop session identifiers onto the desktops this app can drive.
    // Separated from Detect so it can be exercised without a live session.
    internal static LinuxDesktopEnvironment Classify(IEnumerable<string?> candidates)
    {
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrEmpty(candidate))
                continue;
            if (candidate.Contains("kde", StringComparison.OrdinalIgnoreCase)
                || candidate.Contains("plasma", StringComparison.OrdinalIgnoreCase))
                return LinuxDesktopEnvironment.Kde;
            if (candidate.Contains("gnome", StringComparison.OrdinalIgnoreCase)
                || candidate.Contains("unity", StringComparison.OrdinalIgnoreCase)
                || candidate.Contains("cinnamon", StringComparison.OrdinalIgnoreCase))
                return LinuxDesktopEnvironment.Gnome;
        }

        return LinuxDesktopEnvironment.Unknown;
    }

    private static bool IsProcessRunning(string processName)
    {
        try
        {
            return Process.GetProcessesByName(processName).Length > 0;
        }
        catch
        {
            return false;
        }
    }

    // True when the named executable resolves on PATH. Used to pick between the
    // several tools that can set a wallpaper on an unrecognised desktop.
    public static bool CommandExists(string command)
    {
        var pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVariable))
            return false;

        foreach (var directory in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var fullPath = Path.Combine(directory, command);
            if (File.Exists(fullPath))
                return true;
        }
        return false;
    }

    // Starts a long-lived helper (such as swaybg) without waiting for it to exit, and
    // returns the process so the caller can replace it on the next wallpaper switch.
    public static Process? StartDetached(string fileName, params string[] arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo(fileName) { UseShellExecute = false };
            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);
            return Process.Start(startInfo);
        }
        catch
        {
            return null;
        }
    }

    // Runs a command to completion and reports whether it exited cleanly.
    // Output is captured (not inherited) so helper chatter never reaches the app's console.
    public static bool Run(string fileName, params string[] arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo(fileName)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            using var process = Process.Start(startInfo);
            if (process is null)
                return false;

            // Bound the wait so a hung helper cannot stall the wallpaper switch.
            if (!process.WaitForExit(15_000))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
                return false;
            }
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
