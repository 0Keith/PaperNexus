namespace PaperNexus.Core.Platform;

// Per-OS implementation of the two operations the app needs from the desktop shell.
// WallpaperApplier selects the right backend once at construction.
public interface IWallpaperBackend
{
    public bool SetWallpaper(string wallpaperPath);
    public void ApplyFillStyle(WallpaperFillStyle style);
}

// Used when the running platform/desktop has no supported wallpaper mechanism, so the
// rest of the app (downloads, gallery, scheduling) still functions instead of crashing.
public sealed class NoOpWallpaperBackend : IWallpaperBackend
{
    public bool SetWallpaper(string wallpaperPath) => false;

    public void ApplyFillStyle(WallpaperFillStyle style) { }
}
