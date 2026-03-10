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
    Task<string?> SwitchToSpecificAsync(string path);
}

internal sealed class SwitchWallpaper : ISwitchWallpaper, IAddSingleton<ISwitchWallpaper>
{
    private readonly ILogger<SwitchWallpaper> _logger;

    public event Action<string>? WallpaperChanged;

    public SwitchWallpaper(ILogger<SwitchWallpaper> logger)
    {
        _logger = logger.ThrowIfNull();
    }

    // Advances to the next wallpaper according to the configured slideshow order.
    // Banned wallpapers are excluded from the candidate pool. In Random order the
    // current wallpaper is excluded from candidates (unless it is the only one) so the
    // same image is not shown twice in a row. For sequential orders the list wraps around.
    public async Task<string?> SwitchToNextAsync()
    {
        var settings = await WallpaperNexusSettings.LoadAsync().ConfigureAwait(false);
        if (!settings.IsConfigured)
            return null;

        var bannedSet = new HashSet<string>(settings.BannedWallpapers, StringComparer.OrdinalIgnoreCase);
        var allFiles = GetWallpaperFiles(settings.Download.Folder)
            .Where(f => !bannedSet.Contains(f.FullName))
            .ToList();

        if (allFiles.Count == 0)
            return null;

        string next;
        if (settings.Slideshow.Order == SlideshowOrder.Random && allFiles.Count > 1)
        {
            var candidates = allFiles
                .Select(f => f.FullName)
                .Where(f => !f.Equals(settings.CurrentWallpaperPath, StringComparison.OrdinalIgnoreCase))
                .ToList();
            // If removing the current leaves nothing (e.g. only one file), fall back to full list
            if (candidates.Count == 0)
                candidates = allFiles.Select(f => f.FullName).ToList();
            candidates = ApplyFavoritePriority(candidates, settings);
            next = candidates[Random.Shared.Next(candidates.Count)];
        }
        else
        {
            var files = settings.Slideshow.Order switch
            {
                SlideshowOrder.OldestFirst => allFiles.OrderBy(f => f.LastWriteTime).Select(f => f.FullName).ToList(),
                SlideshowOrder.NewestFirst => allFiles.OrderByDescending(f => f.LastWriteTime).Select(f => f.FullName).ToList(),
                _ => allFiles.OrderBy(f => f.Name).Select(f => f.FullName).ToList(),
            };
            var index = files.IndexOf(settings.CurrentWallpaperPath);
            // If persisted wallpaper is not in the folder (index == -1), start from the first file.
            next = files[(index + 1) % files.Count];
        }

        return await ApplyWallpaperAsync(next, settings).ConfigureAwait(false);
    }

    // Picks a random wallpaper from the non-banned set, preferring to avoid repeating
    // the current wallpaper when more than one candidate is available.
    public async Task<string?> SwitchToRandomAsync()
    {
        var settings = await WallpaperNexusSettings.LoadAsync().ConfigureAwait(false);
        if (!settings.IsConfigured)
            return null;

        var bannedSet = new HashSet<string>(settings.BannedWallpapers, StringComparer.OrdinalIgnoreCase);
        var candidates = GetWallpaperFiles(settings.Download.Folder)
            .Select(f => f.FullName)
            .Where(f => !bannedSet.Contains(f))
            .ToList();

        if (candidates.Count == 0)
            return null;

        if (candidates.Count > 1)
        {
            // Exclude the current wallpaper so the user always sees something different
            var withoutCurrent = candidates
                .Where(f => !f.Equals(settings.CurrentWallpaperPath, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (withoutCurrent.Count > 0)
                candidates = withoutCurrent;
        }

        candidates = ApplyFavoritePriority(candidates, settings);
        var next = candidates[Random.Shared.Next(candidates.Count)];
        return await ApplyWallpaperAsync(next, settings).ConfigureAwait(false);
    }

    // Boosts the probability of favorited wallpapers being selected by adding extra
    // copies of each favorite into the candidate pool. The effective probability of
    // a favorite being chosen is roughly weight times that of a non-favorite.
    private static List<string> ApplyFavoritePriority(List<string> candidates, WallpaperNexusSettings settings)
    {
        if (!settings.Slideshow.FavoritePriorityEnabled || settings.FavoriteWallpapers.Count == 0)
            return candidates;

        // Clamp weight to at least 2 so there is always a meaningful boost
        var weight = Math.Max(2, settings.Slideshow.FavoritePriorityWeight);
        var favSet = new HashSet<string>(settings.FavoriteWallpapers, StringComparer.OrdinalIgnoreCase);
        var weighted = new List<string>(candidates);
        foreach (var c in candidates)
        {
            if (favSet.Contains(c))
            {
                // Add weight-1 extra copies (first copy is already in `weighted` from the initial clone)
                for (var i = 1; i < weight; i++)
                    weighted.Add(c);
            }
        }
        return weighted;
    }

    public async Task<string?> SwitchToSpecificAsync(string path)
    {
        if (!File.Exists(path))
            return null;
        var settings = await WallpaperNexusSettings.LoadAsync().ConfigureAwait(false);
        return await ApplyWallpaperAsync(path, settings).ConfigureAwait(false);
    }

    private static List<FileInfo> GetWallpaperFiles(string folder) =>
        new DirectoryInfo(folder)
            .EnumerateFiles()
            .Where(f => f.Extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
                     || f.Extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                     || f.Extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
            .ToList();

    // Applies the chosen wallpaper: optionally composites the title annotation, encodes
    // to the processed current file, sets the Windows desktop wallpaper, and persists the
    // current path to settings so the next run knows where it left off.
    //
    // Write to a fixed current file in the execution directory so the original files are never modified.
    // Apply the title overlay here rather than at download time to preserve source image quality.
    // Save as PNG; if it exceeds 16 MB fall back to JPEG stepping quality down by 3% from 97%.
    private async Task<string?> ApplyWallpaperAsync(string next, WallpaperNexusSettings settings)
    {
        // Strip the URL-stem suffix (" - <urlfile>") that was added during download to recover the human-readable title
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
            // Prefer a bundled font; fall back to the default bundled family if the name is not recognised
            var fontFamily = BundledFonts.TryGet(annotation.FontFamily, out var family)
                ? family : BundledFonts.Collection.Get(BundledFonts.DefaultFontFamily);
            var fontSize = annotation.FontSize > 0 ? annotation.FontSize : 18;
            var font = new Font(fontFamily, fontSize);
            var color = Color.WhiteSmoke;
            try { color = Color.ParseHex(annotation.Color); }
            catch (Exception ex) { _logger.LogWarning(ex, "Invalid annotation color '{Color}', using default.", annotation.Color); }
            // Choose outline colour based on perceived brightness: dark outline for light text, light for dark
            var pixel = color.ToPixel<Rgba32>();
            var outlineColor = pixel.R + pixel.G + pixel.B > 382 ? Color.Black : Color.White;
            var outlinePen = annotation.OutlineEnabled
                ? Pens.Solid(outlineColor, fontSize / 36f)
                : null;
            var brush = new SolidBrush(color);
            // Offset from corner edges by a fixed margin; right-side positions use a symmetric offset from the right
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

            // In debug mode, add a smaller timestamp label immediately below/above the title
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
        // First attempt: lossless PNG with 8-bit RGB (drops alpha, which is never needed for wallpapers)
        await annotated.SaveAsPngAsync(ms, new PngEncoder { ColorType = PngColorType.Rgb, BitDepth = PngBitDepth.Bit8 }).ConfigureAwait(false);
        string currentPath;
        if (ms.Length <= SizeCeiling)
        {
            currentPath = Path.Combine(AppContext.BaseDirectory, "current.png");
            await File.WriteAllBytesAsync(currentPath, ms.ToArray()).ConfigureAwait(false);
            // Remove the alternate format file so Windows doesn't pick up a stale version
            File.Delete(Path.Combine(AppContext.BaseDirectory, "current.jpg"));
        }
        else
        {
            // PNG is too large (high-res 4K+); re-encode as JPEG, reducing quality until it fits under 16 MB
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

        // Persist the original source path (not the processed current.* path) so ordering is stable across restarts
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

    // Returns an empty config (no schedule) when the slideshow is disabled.
    // Otherwise parses the stored cron expression to schedule automatic wallpaper rotation.
    public async Task<JobConfig> GetJobConfigAsync()
    {
        var settings = await WallpaperNexusSettings.LoadAsync();
        if (!settings.Slideshow.Enabled)
            return new JobConfig();
        var stored = settings.Slideshow.CronExpression;
        var fields = stored.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var format = fields.Length == 6 ? CronFormat.IncludeSeconds : CronFormat.Standard;
        var cronExpression = CronExpression.Parse(stored, format);
        return new JobConfig(CronExpression: cronExpression);
    }

    public async Task ExecuteAsync()
    {
        var next = await _switcher.SwitchToNextAsync();
        if (next is null)
            _logger.LogInformation("Wallpapers folder not configured or no wallpapers found — skipping.");
    }
}
