namespace PaperNexus.Core.Platform;

// Sets the desktop wallpaper on Linux. There is no cross-desktop API for this, so the
// backend dispatches to the mechanism the running desktop environment provides:
//   KDE Plasma (the SteamOS Desktop Mode shell) - a JavaScript snippet run through
//                                                 plasmashell's D-Bus evaluateScript
//   GNOME                                       - gsettings keys under org.gnome.desktop.background
//   anything else                               - feh / xwallpaper / swaybg if installed
public sealed class LinuxWallpaperBackend : IWallpaperBackend
{
    private readonly LinuxDesktopEnvironment _desktop = LinuxDesktop.Detect();

    // The fill style is applied by the same command that sets the image on KDE, so it is
    // cached here and replayed whenever either half changes.
    private WallpaperFillStyle _fillStyle = WallpaperFillStyle.Fill;
    private string? _currentPath;

    public bool SetWallpaper(string wallpaperPath)
    {
        if (!File.Exists(wallpaperPath))
            return false;

        _currentPath = wallpaperPath;
        return Apply(wallpaperPath, _fillStyle);
    }

    public void ApplyFillStyle(WallpaperFillStyle style)
    {
        _fillStyle = style;
        // Re-apply against the image already on screen so the new style takes effect
        // immediately rather than at the next scheduled switch.
        if (_currentPath is not null)
            Apply(_currentPath, style);
    }

    private bool Apply(string wallpaperPath, WallpaperFillStyle style) => _desktop switch
    {
        LinuxDesktopEnvironment.Kde => ApplyKde(wallpaperPath, style),
        LinuxDesktopEnvironment.Gnome => ApplyGnome(wallpaperPath, style),
        _ => ApplyGeneric(wallpaperPath, style),
    };

    // KDE Plasma stores the wallpaper in each containment's config. Plasma only repaints
    // when the stored value actually changes, and PaperNexus always writes to the same
    // current.png, so the script clears the Image key before writing the real path.
    private static bool ApplyKde(string wallpaperPath, WallpaperFillStyle style)
    {
        var fillMode = style switch
        {
            WallpaperFillStyle.Stretch => 0,
            WallpaperFillStyle.Fit => 1,
            WallpaperFillStyle.Fill => 2,
            WallpaperFillStyle.Span => 2,
            WallpaperFillStyle.Tile => 3,
            WallpaperFillStyle.Center => 6,
            _ => 2,
        };

        var imageUri = new Uri(wallpaperPath).AbsoluteUri;
        var script = $$"""
            var screens = desktops();
            for (var i = 0; i < screens.length; i++) {
                var desktop = screens[i];
                desktop.wallpaperPlugin = "org.kde.image";
                desktop.currentConfigGroup = Array("Wallpaper", "org.kde.image", "General");
                desktop.writeConfig("FillMode", {{fillMode}});
                desktop.writeConfig("Image", "");
                desktop.writeConfig("Image", "{{imageUri}}");
            }
            """;

        // The qdbus binary is named differently across Qt versions and distributions.
        foreach (var qdbus in new[] { "qdbus6", "qdbus-qt6", "qdbus", "qdbus-qt5" })
        {
            if (!LinuxDesktop.CommandExists(qdbus))
                continue;
            if (LinuxDesktop.Run(qdbus, "org.kde.plasmashell", "/PlasmaShell", "org.kde.PlasmaShell.evaluateScript", script))
                return true;
        }

        // Fall back to the shipped CLI tool, which sets the image but not the fill mode.
        if (LinuxDesktop.CommandExists("plasma-apply-wallpaperimage"))
            return LinuxDesktop.Run("plasma-apply-wallpaperimage", wallpaperPath);

        return false;
    }

    // GNOME reads the wallpaper from GSettings. Both the light and dark keys must be set,
    // otherwise the wallpaper appears to not change under the dark colour scheme.
    private static bool ApplyGnome(string wallpaperPath, WallpaperFillStyle style)
    {
        if (!LinuxDesktop.CommandExists("gsettings"))
            return false;

        var pictureOption = style switch
        {
            WallpaperFillStyle.Tile => "wallpaper",
            WallpaperFillStyle.Center => "centered",
            WallpaperFillStyle.Stretch => "stretched",
            WallpaperFillStyle.Fit => "scaled",
            WallpaperFillStyle.Fill => "zoom",
            WallpaperFillStyle.Span => "spanned",
            _ => "zoom",
        };

        var imageUri = new Uri(wallpaperPath).AbsoluteUri;
        const string schema = "org.gnome.desktop.background";

        LinuxDesktop.Run("gsettings", "set", schema, "picture-options", pictureOption);

        // Clearing first forces a repaint when the path is unchanged from the previous switch.
        var applied = false;
        foreach (var key in new[] { "picture-uri", "picture-uri-dark" })
        {
            LinuxDesktop.Run("gsettings", "set", schema, key, "");
            applied |= LinuxDesktop.Run("gsettings", "set", schema, key, imageUri);
        }
        return applied;
    }

    // swaybg runs for as long as the wallpaper is displayed, so the previous instance is
    // kept here and terminated before a replacement is started.
    private System.Diagnostics.Process? _swaybg;

    // Standalone setters used by minimal window managers. Each is tried in turn.
    private bool ApplyGeneric(string wallpaperPath, WallpaperFillStyle style)
    {
        if (LinuxDesktop.CommandExists("feh"))
        {
            var mode = style switch
            {
                WallpaperFillStyle.Tile => "--bg-tile",
                WallpaperFillStyle.Center => "--bg-center",
                WallpaperFillStyle.Stretch => "--bg-scale",
                WallpaperFillStyle.Fit => "--bg-max",
                _ => "--bg-fill",
            };
            if (LinuxDesktop.Run("feh", "--no-fehbg", mode, wallpaperPath))
                return true;
        }

        if (LinuxDesktop.CommandExists("xwallpaper"))
        {
            var mode = style switch
            {
                WallpaperFillStyle.Tile => "--tile",
                WallpaperFillStyle.Center => "--center",
                WallpaperFillStyle.Stretch => "--stretch",
                WallpaperFillStyle.Fit => "--maximize",
                _ => "--zoom",
            };
            if (LinuxDesktop.Run("xwallpaper", mode, wallpaperPath))
                return true;
        }

        if (LinuxDesktop.CommandExists("swaybg"))
        {
            var mode = style switch
            {
                WallpaperFillStyle.Tile => "tile",
                WallpaperFillStyle.Center => "center",
                WallpaperFillStyle.Stretch => "stretch",
                WallpaperFillStyle.Fit => "fit",
                _ => "fill",
            };

            var replacement = LinuxDesktop.StartDetached("swaybg", "-i", wallpaperPath, "-m", mode);
            if (replacement is null)
                return false;

            // Kill the old instance only after the replacement is up, to avoid a visible
            // gap where no wallpaper is drawn at all.
            var previous = _swaybg;
            _swaybg = replacement;
            if (previous is not null)
            {
                try { previous.Kill(entireProcessTree: true); } catch { /* already exited */ }
                previous.Dispose();
            }
            return true;
        }

        return false;
    }
}
