using Cronos;

namespace PaperNexus;

internal interface IDownloadWallpapers
{
    Task DownloadAllAsync();
}

internal class DownloadWallpapers : ScheduledJobService, IDownloadWallpapers, IAddHostedSingleton<IDownloadWallpapers>
{
    private readonly HttpWallpaperSourceService _sourceService;

    public DownloadWallpapers(ILogger<DownloadWallpapers> logger, HttpWallpaperSourceService sourceService) : base(logger)
    {
        _sourceService = sourceService.ThrowIfNull();
        ExecuteOnStartup = true;
    }

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

    protected override Task Execute() => DownloadFromSourcesAsync(source =>
    {
        if (!IsOverdue(source))
        {
            Logger.LogInformation($"Source '{source.Name}' is up to date — skipping.");
            return false;
        }
        return true;
    });

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

    private static bool IsOverdue(WallpaperSource source)
    {
        if (source.LastDownloadUtc is null)
            return true;

        try
        {
            var cron = CronExpression.Parse(source.CronExpression);
            var next = cron.GetNextOccurrence(source.LastDownloadUtc.Value, TimeZoneInfo.Local);
            return next.HasValue && next.Value <= DateTimeOffset.UtcNow;
        }
        catch (CronFormatException)
        {
            return true;
        }
    }

    public async Task Download(WallpaperImage data, WallpaperNexusSettings settings)
    {
        var invalidChars = Path.GetInvalidFileNameChars().Concat(Path.GetInvalidPathChars()).ToHashSet();
        var title = new string(data.Title
            .Where(c => !invalidChars.Contains(c))
            .Take(200)
            .ToArray());
        var urlFile = data.ImageUrl.Split('/').Last();
        var ext = Path.GetExtension(urlFile);
        if (string.IsNullOrEmpty(ext))
            ext = ".png";
        title += " - " + Path.GetFileNameWithoutExtension(urlFile);
        var path = $"{settings.Download.Folder}/{title}{ext}";
        if (!Debugger.IsAttached && File.Exists(path))
            return;

        Logger.LogInformation($"Downloading Image: {data.Title}");
        var watch = Stopwatch.StartNew();
        using var client = new HttpClient();
        using var response = await client.GetAsync(data.ImageUrl, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode)
        {
            var message = await response.Content.ReadAsStringAsync();
            throw new Exception($"{response.StatusCode} : {message}");
        }

        var bytes = await response.Content.ReadAsByteArrayAsync();
        await File.WriteAllBytesAsync(path, bytes);
        Logger.LogInformation($"Download Complete: {watch.Elapsed}");
    }

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
