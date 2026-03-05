using Cronos;
using PaperNexus.Core;
using Microsoft.Win32;
using SixLabors.Fonts;
using BundledFonts = PaperNexus.Core.BundledFonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;
using System.Runtime.InteropServices;

namespace PaperNexus;

public interface ISwitchWallpaper
{
    event Action<string>? WallpaperChanged;
    Task<string?> SwitchToNextAsync();
    Task<string?> SwitchToRandomAsync();
}

internal sealed class SwitchWallpaper : ISwitchWallpaper, IAddSingleton<ISwitchWallpaper>
{
    private readonly ILogger<SwitchWallpaper> _logger;

    public event Action<string>? WallpaperChanged;

    public SwitchWallpaper(ILogger<SwitchWallpaper> logger)
    {
        _logger = logger.ThrowIfNull();
    }

    public async Task<string?> SwitchToNextAsync()
    {
        var settings = await WallpaperNexusSettings.LoadAsync().ConfigureAwait(false);
        if (!settings.IsConfigured)
            return null;

        var allFiles = GetWallpaperFiles(settings.Download.Folder);

        if (allFiles.Count == 0)
            return null;

        string next;
        if (settings.Slideshow.Order == SlideshowOrder.Random && allFiles.Count > 1)
        {
            var candidates = allFiles
                .Select(f => f.FullName)
                .Where(f => !f.Equals(settings.CurrentWallpaperPath, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (candidates.Count == 0)
                candidates = allFiles.Select(f => f.FullName).ToList();
            next = candidates[Random.Shared.Next(candidates.Count)];
        }
        else
        {
            var files = settings.Slideshow.Order switch
            {
                SlideshowOrder.OldestFirst => allFiles.OrderBy(f => f.LastWriteTime).Select(f => f.FullName).ToList(),
                SlideshowOrder.NewestFirst => allFiles.OrderByDescending(f => f.LastWriteTime).Select(f => f.FullName).ToList(),
                _ => allFiles.OrderBy(f => f.Name).Select(f => f.FullName).ToList(), // Sequential, Alphabetical, default
            };
            var index = files.IndexOf(settings.CurrentWallpaperPath);
            // If persisted wallpaper is not in the folder (index == -1), start from the first file.
            next = files[(index + 1) % files.Count];
        }

        return await ApplyWallpaperAsync(next, settings).ConfigureAwait(false);
    }

    public async Task<string?> SwitchToRandomAsync()
    {
        var settings = await WallpaperNexusSettings.LoadAsync().ConfigureAwait(false);
        if (!settings.IsConfigured)
            return null;

        var allFiles = GetWallpaperFiles(settings.Download.Folder);

        if (allFiles.Count == 0)
            return null;

        var candidates = allFiles.Select(f => f.FullName).ToList();
        if (candidates.Count > 1)
            candidates = candidates.Where(f => !f.Equals(settings.CurrentWallpaperPath, StringComparison.OrdinalIgnoreCase)).ToList();

        var next = candidates[Random.Shared.Next(candidates.Count)];
        return await ApplyWallpaperAsync(next, settings).ConfigureAwait(false);
    }

    private static List<FileInfo> GetWallpaperFiles(string folder) =>
        new DirectoryInfo(folder)
            .EnumerateFiles()
            .Where(f => f.Extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
                     || f.Extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                     || f.Extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
            .ToList();

    private async Task<string?> ApplyWallpaperAsync(string next, WallpaperNexusSettings settings)
    {
        // Write to a fixed current file in the execution directory so the original files are never modified.
        // Apply the title overlay here rather than at download time to preserve source image quality.
        // Save as PNG; if it exceeds 16 MB fall back to JPEG stepping quality down by 3% from 97%.
        var title = Path.GetFileNameWithoutExtension(next);
        var separatorIndex = title.LastIndexOf(" - ", StringComparison.Ordinal);
        if (separatorIndex >= 0)
            title = title[..separatorIndex];
        using var img = await Image.LoadAsync(next).ConfigureAwait(false);
        using var annotated = img.Clone(o =>
        {
            if (!settings.AnnotateWallpaper)
                return;
            var annotation = settings.Annotation;
            var fontFamily = BundledFonts.TryGet(annotation.FontFamily, out var family)
                ? family : BundledFonts.Collection.Get(BundledFonts.DefaultFontFamily);
            var fontSize = annotation.FontSize > 0 ? annotation.FontSize : 18;
            var font = new Font(fontFamily, fontSize);
            var color = Color.WhiteSmoke;
            try { color = Color.ParseHex(annotation.Color); }
            catch { }
            var pixel = color.ToPixel<Rgba32>();
            var outlineColor = pixel.R + pixel.G + pixel.B > 382 ? Color.Black : Color.White;
            var outlinePen = annotation.OutlineEnabled
                ? Pens.Solid(outlineColor, Math.Max(1, fontSize / 17f))
                : null;
            var brush = new SolidBrush(color);
            var position = annotation.Position switch
            {
                AnnotationPosition.TopRight => new PointF(img.Width - 125, 5),
                AnnotationPosition.BottomLeft => new PointF(125, img.Height - fontSize - 10),
                AnnotationPosition.BottomRight => new PointF(img.Width - 125, img.Height - fontSize - 10),
                _ => new PointF(125, 5),
            };
            var options = new RichTextOptions(font) { Origin = position };
            if (annotation.Position is AnnotationPosition.TopRight or AnnotationPosition.BottomRight)
                options.HorizontalAlignment = HorizontalAlignment.Right;
            o.DrawText(options, title, brush, outlinePen);

            if (settings.DebugMode)
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                var tsFont = new Font(fontFamily, fontSize * 0.75f);
                var tsY = annotation.Position is AnnotationPosition.TopLeft or AnnotationPosition.TopRight
                    ? position.Y + fontSize + 4
                    : position.Y - fontSize;
                var tsOptions = new RichTextOptions(tsFont) { Origin = new PointF(position.X, tsY) };
                if (annotation.Position is AnnotationPosition.TopRight or AnnotationPosition.BottomRight)
                    tsOptions.HorizontalAlignment = HorizontalAlignment.Right;
                o.DrawText(tsOptions, timestamp, brush, outlinePen);
            }
        });
        using var ms = new MemoryStream();
        await annotated.SaveAsPngAsync(ms, new PngEncoder { ColorType = PngColorType.Rgb, BitDepth = PngBitDepth.Bit8 }).ConfigureAwait(false);
        string currentPath;
        if (ms.Length <= SizeCeiling)
        {
            currentPath = Path.Combine(AppContext.BaseDirectory, "current.png");
            await File.WriteAllBytesAsync(currentPath, ms.ToArray()).ConfigureAwait(false);
            File.Delete(Path.Combine(AppContext.BaseDirectory, "current.jpg"));
        }
        else
        {
            currentPath = Path.Combine(AppContext.BaseDirectory, "current.jpg");
            for (var quality = 97; quality >= 1; quality -= 3)
            {
                ms.SetLength(0);
                await annotated.SaveAsJpegAsync(ms, new JpegEncoder { Quality = quality }).ConfigureAwait(false);
                if (ms.Length <= SizeCeiling)
                    break;
            }
            await File.WriteAllBytesAsync(currentPath, ms.ToArray()).ConfigureAwait(false);
            File.Delete(Path.Combine(AppContext.BaseDirectory, "current.png"));
        }

        if (OperatingSystem.IsWindows())
            ApplyFillStyle(settings.Slideshow.FillStyle);
        NativeMethods.SetDesktopWallpaper(currentPath);
        _logger.LogInformation($"Switching wallpaper to: {next}");

        settings.CurrentWallpaperPath = next;
        await settings.SaveAsync().ConfigureAwait(false);
        WallpaperChanged?.Invoke(next);
        return next;
    }

    private const long SizeCeiling = 1 << 24; // 16 MB

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void ApplyFillStyle(WallpaperFillStyle style)
    {
        // WallpaperStyle and TileWallpaper registry values under HKCU\Control Panel\Desktop
        // control how Windows positions the wallpaper image.
        var (wallpaperStyle, tileWallpaper) = style switch
        {
            WallpaperFillStyle.Tile => ("0", "1"),
            WallpaperFillStyle.Center => ("0", "0"),
            WallpaperFillStyle.Stretch => ("2", "0"),
            WallpaperFillStyle.Fit => ("6", "0"),
            WallpaperFillStyle.Fill => ("10", "0"),
            WallpaperFillStyle.Span => ("22", "0"),
            _ => ("10", "0"),
        };

        using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", writable: true);
        key?.SetValue("WallpaperStyle", wallpaperStyle);
        key?.SetValue("TileWallpaper", tileWallpaper);
    }
}

internal sealed class SwitchWallpaperJob : IScheduleScopedJob
{
    private readonly ISwitchWallpaper _switcher;
    private readonly ILogger<SwitchWallpaperJob> _logger;

    public SwitchWallpaperJob(ISwitchWallpaper switcher, ILogger<SwitchWallpaperJob> logger)
    {
        _switcher = switcher.ThrowIfNull();
        _logger = logger.ThrowIfNull();
    }

    public async Task<JobConfig> GetJobConfigAsync()
    {
        var settings = await WallpaperNexusSettings.LoadAsync();
        if (!settings.Slideshow.Enabled)
            return new JobConfig();
        var cronExpression = CronExpression.Parse(settings.Slideshow.CronExpression);
        return new JobConfig(CronExpression: cronExpression);
    }

    public async Task ExecuteAsync()
    {
        var next = await _switcher.SwitchToNextAsync();
        if (next is null)
            _logger.LogInformation("Wallpapers folder not configured or no wallpapers found — skipping.");
    }
}
