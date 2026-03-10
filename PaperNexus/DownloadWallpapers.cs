using Cronos;

namespace PaperNexus;

internal interface IDownloadWallpapers
{
    Task DownloadAllAsync();
}

internal class DownloadWallpapers : ScheduledJobService, IDownloadWallpapers, IAddHostedSingleton<IDownloadWallpapers>
{
    private static readonly HashSet<char> InvalidFileNameChars = Path.GetInvalidFileNameChars()
        .Concat(Path.GetInvalidPathChars())
        .Append('/').Append('\\')
        .ToHashSet();

    private readonly HttpWallpaperSourceService _sourceService;

    public DownloadWallpapers(ILogger<DownloadWallpapers> logger, HttpWallpaperSourceService sourceService) : base(logger)
    {
        _sourceService = sourceService.ThrowIfNull();
        ExecuteOnStartup = true;
    }

    // Returns the soonest upcoming cron occurrence across all enabled sources so the
    // legacy scheduler wakes up at the right time. Falls back to 1 hour if no source
    // has a next occurrence within that window.
    protected override async Task<DateTimeOffset> GetNextExecutionAsync(JobExecutionContext context)
    {
        var settings = await WallpaperNexusSettings.LoadAsync();
        var earliest = DateTimeOffset.Now.AddHours(1);
        foreach (var source in settings.Sources.Where(s => s.IsEnabled))
        {
            var expression = CronExpression.Parse(source.CronExpression);
            var next = expression.GetNextOccurrence(DateTimeOffset.UtcNow, TimeZoneInfo.Local);
            if (next.HasValue && next.Value < earliest)
                earliest = next.Value;
        }
        return earliest;
    }

    public Task DownloadAllAsync() => DownloadFromSourcesAsync(_ => true);

    // Scheduled execution: only downloads from sources whose cron interval has elapsed
    // since the last successful download, skipping recently-updated sources.
    protected override Task Execute() => DownloadFromSourcesAsync(source =>
    {
        if (!IsOverdue(source))
        {
            Logger.LogInformation($"Source '{source.Name}' is up to date — skipping.");
            return false;
        }
        return true;
    });

    // Core download loop: iterates enabled sources that pass the caller-supplied filter,
    // then triggers retention cleanup and persists the updated LastDownloadUtc timestamps.
    // Cleanup and save are skipped entirely if no sources were actually downloaded.
    private async Task DownloadFromSourcesAsync(Func<WallpaperSource, bool> filter)
    {
        var settings = await WallpaperNexusSettings.LoadAsync();
        if (!settings.IsConfigured)
        {
            Logger.LogInformation("Wallpapers folder not configured — skipping.");
            return;
        }

        Directory.CreateDirectory(settings.Download.Folder);

        var downloaded = false;
        foreach (var source in settings.Sources.Where(s => s.IsEnabled && filter(s)))
        {
            try
            {
                await DownloadSource(source, settings);
                downloaded = true;
            }
            catch (Exception ex)
            {
                // Log and continue so a failed source (e.g. unreachable feed URL) does not
                // block remaining sources from running or prevent timestamps from being saved.
                Logger.LogError(ex, "Failed to download from source '{Source}' — skipping.", source.Name);
            }
        }
        if (downloaded)
        {
            await CleanupOldImages(settings);
            await settings.SaveAsync();
        }
    }

    // Downloads all images for a single source using a shared HttpClient for the batch,
    // so N images from the same host reuse one socket pool rather than creating N pools.
    private async Task DownloadSource(WallpaperSource source, WallpaperNexusSettings settings)
    {
        var images = await _sourceService.GetImages(source);
        // A single client is shared across all images in this source batch for socket efficiency.
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        foreach (var image in images)
        {
            try
            {
                await Download(image, settings, client);
            }
            catch (Exception ex)
            {
                // Log and continue so one failed image does not abort the rest of the source's images.
                // The source's LastDownloadUtc is still updated below so it is not retried immediately.
                Logger.LogWarning(ex, "Failed to download image '{Title}' from source '{Source}' — skipping.", image.Title, source.Name);
            }
        }
        source.LastDownloadUtc = DateTimeOffset.UtcNow;
    }

    // Returns true if the source has never been downloaded, or if the next cron occurrence
    // after the last download has already passed. An invalid cron expression is treated
    // as always-overdue so a misconfigured source never silently stalls.
    private static bool IsOverdue(WallpaperSource source)
    {
        if (source.LastDownloadUtc is null)
            return true;

        try
        {
            var cron = CronExpression.Parse(source.CronExpression);
            // Compute the next fire time relative to the last successful download
            var next = cron.GetNextOccurrence(source.LastDownloadUtc.Value, TimeZoneInfo.Local);
            return next.HasValue && next.Value <= DateTimeOffset.UtcNow;
        }
        catch (CronFormatException)
        {
            return true;
        }
    }

    // Downloads a single wallpaper image to the configured folder.
    // The filename is derived from the sanitised title plus the URL's filename component
    // so that the original source identifier is preserved for deduplication.
    // An optional HttpClient may be supplied by the caller to share a socket pool across
    // multiple images; if omitted a short-lived client is created for this call only.
    public async Task Download(WallpaperImage data, WallpaperNexusSettings settings, HttpClient? client = null)
    {
        // Strip characters that are invalid in file names, and cap length to avoid MAX_PATH issues
        var title = new string(data.Title
            .Where(c => !InvalidFileNameChars.Contains(c))
            .Take(200)
            .ToArray());
        var urlFile = data.ImageUrl.Split('/').Last();
        var ext = Path.GetExtension(urlFile);
        if (string.IsNullOrEmpty(ext))
            ext = ".png";
        // Append the URL filename stem so re-downloads of the same title remain distinct
        title += " - " + Path.GetFileNameWithoutExtension(urlFile);
        var path = Path.Combine(settings.Download.Folder, title + ext);

        // Guard against a malicious title that contains ".." path traversal sequences
        var fullPath = Path.GetFullPath(path);
        var folder = Path.GetFullPath(settings.Download.Folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
        {
            Logger.LogWarning("Path traversal blocked: {Path}", fullPath);
            return;
        }
        // Skip existing files unless a debugger is attached (useful for re-testing download logic)
        if (!Debugger.IsAttached && File.Exists(path))
            return;

        Logger.LogInformation($"Downloading Image: {data.Title}");
        var watch = Stopwatch.StartNew();
        // Use the caller-supplied client when available; otherwise create a short-lived one
        // (the caller is responsible for disposing a shared client).
        var ownClient = client is null;
        var httpClient = client ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        try
        {
            // Use ResponseHeadersRead to start timing from the first byte, not after full download
            using var response = await httpClient.GetAsync(data.ImageUrl, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
            {
                var message = await response.Content.ReadAsStringAsync();
                throw new HttpRequestException($"HTTP {(int)response.StatusCode} {response.StatusCode} downloading '{data.ImageUrl}': {message}");
            }

            // Stream directly to disk so large images (4K+) don't require a full in-memory buffer
            using var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true);
            await response.Content.CopyToAsync(fileStream);
            Logger.LogInformation($"Download Complete: {watch.Elapsed}");
        }
        finally
        {
            // Only dispose the client if we created it; callers own shared clients
            if (ownClient)
                httpClient.Dispose();
        }
    }

    // Deletes wallpaper files older than the configured retention period.
    // Favorited files are excluded from cleanup regardless of age.
    private async Task CleanupOldImages(WallpaperNexusSettings settings)
    {
        var favorites = new HashSet<string>(
            settings.FavoriteWallpapers ?? [],
            StringComparer.OrdinalIgnoreCase);
        var files = new DirectoryInfo(settings.Download.Folder).EnumerateFiles();
        var cutoff = DateTime.UtcNow.AddDays(-settings.Download.RetentionDays);
        foreach (var file in files)
            if (cutoff > file.LastWriteTimeUtc && !favorites.Contains(file.FullName))
                file.Delete();
    }
}
