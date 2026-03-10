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
            await DownloadSource(source, settings);
            downloaded = true;
        }
        if (downloaded)
        {
            await CleanupOldImages(settings);
            await settings.SaveAsync();
        }
    }

    private async Task DownloadSource(WallpaperSource source, WallpaperNexusSettings settings)
    {
        var images = await _sourceService.GetImages(source);
        foreach (var image in images)
            await Download(image, settings);
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
    public async Task Download(WallpaperImage data, WallpaperNexusSettings settings)
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
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        // Use ResponseHeadersRead to start timing from the first byte, not after full download
        using var response = await client.GetAsync(data.ImageUrl, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode)
        {
            var message = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"HTTP {(int)response.StatusCode} {response.StatusCode} downloading '{data.ImageUrl}': {message}");
        }

        var bytes = await response.Content.ReadAsByteArrayAsync();
        await File.WriteAllBytesAsync(path, bytes);
        Logger.LogInformation($"Download Complete: {watch.Elapsed}");
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
