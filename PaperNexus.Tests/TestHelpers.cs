using PaperNexus.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PaperNexus.Tests;

internal static class TestHelpers
{
    internal static readonly string BaseDir = AppContext.BaseDirectory;
    internal static readonly string PngPath = Path.Combine(BaseDir, "current.png");
    internal static readonly string JpgPath = Path.Combine(BaseDir, "current.jpg");

    internal static void CreateSmallPng(string path, byte r = 100, byte g = 150, byte b = 200)
    {
        using var img = new Image<Rgb24>(100, 100, new Rgb24(r, g, b));
        img.SaveAsPng(path);
    }

    internal static void CreateSmallJpeg(string path, byte r = 200, byte g = 100, byte b = 50)
    {
        using var img = new Image<Rgb24>(100, 100, new Rgb24(r, g, b));
        img.SaveAsJpeg(path);
    }

    /// <summary>
    /// Creates a large PNG with random pixel data that exceeds 16 MB when
    /// re-encoded as RGB8 PNG, forcing the JPEG fallback in ApplyWallpaperAsync.
    /// </summary>
    internal static void CreateOversizedPng(string path)
    {
        var rng = new Random(42);
        using var img = new Image<Rgb24>(4000, 4000);
        img.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                    row[x] = new Rgb24((byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256));
            }
        });
        img.SaveAsPng(path);
    }

    internal static async Task WriteSettingsAsync(string wallpaperFolder, string currentWallpaperPath = "")
    {
        var settings = new WallpaperNexusSettings
        {
            Slideshow = new SlideshowSettings
            {
                Enabled = false,
                Order = SlideshowOrder.Alphabetical,
            },
            Download = new DownloadSettings { Folder = wallpaperFolder },
            CurrentWallpaperPath = currentWallpaperPath,
            AnnotateWallpaper = false,
            RunOnStartup = false,
            AutoUpdatesEnabled = false,
            Sources = [],
        };
        await settings.SaveAsync();
    }

    internal static void Cleanup()
    {
        TryDelete(PngPath);
        TryDelete(JpgPath);
        TryDelete(WallpaperNexusSettings.SettingsFilePath);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { }
    }
}
