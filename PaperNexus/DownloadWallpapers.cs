using Cronos;

namespace PaperNexus;

internal class DownloadWallpapers : ScheduledJobService
{
    private readonly HttpWallpaperSourceService _sourceService;

    public DownloadWallpapers(ILogger<DownloadWallpapers> logger, HttpWallpaperSourceService sourceService) : base(logger)
    {
        _sourceService = sourceService.ThrowIfNull();
        ExecuteOnStartupAfterFailure = true;
        DebugOnStartup = true;
    }

    protected override async Task<DateTimeOffset> GetNextExecutionAsync(JobExecutionContext context)
    {
        var settings = await WallpaperNexusSettings.LoadAsync();
        var earliest = DateTimeOffset.Now.AddHours(1);
        foreach (var source in settings.Sources)
        {
            var expression = CronExpression.Parse(source.CronExpression);
            var next = expression.GetNextOccurrence(DateTimeOffset.UtcNow, TimeZoneInfo.Local);
            if (next.HasValue && next.Value < earliest)
                earliest = next.Value;
        }
        return earliest;
    }

    protected override async Task Execute()
    {
        var settings = await WallpaperNexusSettings.LoadAsync();
        if (!settings.IsConfigured)
        {
            Logger.LogInformation("Wallpapers folder not configured — skipping.");
            return;
        }

        foreach (var source in settings.Sources)
        {
            var images = await _sourceService.GetImages(source);
            foreach (var image in images)
                await Download(image, settings);
        }
        await CleanupOldImages(settings);
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
        var path = $"{settings.WallpapersFolder}/{title}{ext}";
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
        var files = new DirectoryInfo(settings.WallpapersFolder).EnumerateFiles();
        var cutoff = DateTime.UtcNow.AddDays(-settings.RetentionDays);
        foreach (var file in files)
            if (cutoff > file.LastWriteTimeUtc)
                file.Delete();
    }
}
