using PaperNexus.Core.Platform;
using Xunit;

namespace PaperNexus.Tests;

// Verifies the launcher paths and identity that let a pinned dock entry survive the app
// closing. Nothing here writes to the real applications or icon directories: the tests
// redirect XDG_DATA_HOME to a temporary directory.
public class DesktopEntryTests : IDisposable
{
    private readonly string _dataHome;
    private readonly string? _originalDataHome;

    public DesktopEntryTests()
    {
        _originalDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        _dataHome = Path.Combine(Path.GetTempPath(), $"papernexus-xdg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dataHome);
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", _dataHome);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", _originalDataHome);
        try { Directory.Delete(_dataHome, recursive: true); }
        catch (IOException) { /* temp cleanup is best-effort */ }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void WindowClass_MatchesTheLauncherBasename()
    {
        // The desktop shell matches a window to its launcher by comparing the window's
        // WM_CLASS against StartupWMClass and the .desktop basename. If these drift apart,
        // a running window opens a second dock item beside the pinned one.
        Assert.Equal(DesktopEntry.EntryName, DesktopEntry.WindowClass);
        Assert.Equal($"{DesktopEntry.WindowClass}.desktop", Path.GetFileName(DesktopEntry.LauncherPath));
    }

    [Fact]
    public void LauncherPath_HonoursXdgDataHome()
    {
        var expected = Path.Combine(_dataHome, "applications", "PaperNexus.desktop");
        Assert.Equal(expected, DesktopEntry.LauncherPath);
    }

    [Fact]
    public void IconPath_UsesTheHicolorFallbackThemeAtTheAdvertisedSize()
    {
        // hicolor is the theme every desktop falls back to, and the icon must physically
        // sit in the size directory it claims or the lookup misses it.
        var expected = Path.Combine(_dataHome, "icons", "hicolor", "256x256", "apps", "papernexus.png");
        Assert.Equal(expected, DesktopEntry.IconPath);
    }

    [Fact]
    public void Install_WritesALauncherPointingAtTheGivenExecutable()
    {
        if (!OperatingSystem.IsLinux())
            return; // Install is a deliberate no-op off Linux.

        DesktopEntry.Install("/opt/PaperNexus/PaperNexus");

        var contents = File.ReadAllText(DesktopEntry.LauncherPath);
        Assert.Contains("Exec=\"/opt/PaperNexus/PaperNexus\"", contents);
        Assert.Contains("StartupWMClass=PaperNexus", contents);
        Assert.Contains("Type=Application", contents);
    }

    [Fact]
    public void Install_IsIdempotentAndRefreshesTheExecutablePath()
    {
        if (!OperatingSystem.IsLinux())
            return;

        // Called on every launch, so a moved executable must correct the stale Exec line
        // rather than leaving a launcher that starts nothing.
        DesktopEntry.Install("/old/location/PaperNexus");
        DesktopEntry.Install("/new/location/PaperNexus");

        var contents = File.ReadAllText(DesktopEntry.LauncherPath);
        Assert.Contains("Exec=\"/new/location/PaperNexus\"", contents);
        Assert.DoesNotContain("/old/location/", contents);
    }
}
