using Cronos;
using PaperNexus.Core;
using System.Diagnostics;
using System.Text.Json;

namespace PaperNexus;

internal sealed class AutoUpdateService : IScheduleScopedJob
{
    private const string GitHubRepo = "0Keith/PaperNexus";
    private const string AssetName = "PaperNexus.exe";

    private readonly ILogger<AutoUpdateService> _logger;

    public AutoUpdateService(ILogger<AutoUpdateService> logger)
    {
        _logger = logger.ThrowIfNull();
    }

    public Task<JobConfig> GetJobConfigAsync() =>
        Task.FromResult(new JobConfig(
            CronExpression: CronExpression.Parse("0 3 * * *"),
            ExecuteOnStartup: true));

    public async Task ExecuteAsync()
    {
        var current = typeof(AutoUpdateService).Assembly.GetName().Version;
        if (current is null)
            return;

        _logger.LogInformation("Checking for updates. Current version: {Version}", current);

        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PaperNexus-AutoUpdater");

        string json;
        try
        {
            json = await client.GetStringAsync(
                $"https://api.github.com/repos/{GitHubRepo}/releases/latest");
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Update check failed: {Message}", ex.Message);
            return;
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("tag_name", out var tagElement))
            return;

        var tag = tagElement.GetString();
        if (tag is null)
            return;

        var versionStr = tag.TrimStart('v');
        if (!Version.TryParse(versionStr, out var latest))
            return;

        if (latest <= current)
        {
            _logger.LogInformation("Already up to date ({Version})", current);
            return;
        }

        _logger.LogInformation("Update available: {Latest} (current: {Current})", latest, current);

        if (!root.TryGetProperty("assets", out var assetsElement))
            return;

        string? downloadUrl = null;
        foreach (var asset in assetsElement.EnumerateArray())
        {
            if (asset.TryGetProperty("name", out var nameEl) && nameEl.GetString() == AssetName
                && asset.TryGetProperty("browser_download_url", out var urlEl))
            {
                downloadUrl = urlEl.GetString();
                break;
            }
        }

        if (downloadUrl is null)
        {
            _logger.LogWarning("Asset '{Asset}' not found in release {Tag}", AssetName, tag);
            return;
        }

        var exePath = Environment.ProcessPath
            ?? Path.Combine(AppContext.BaseDirectory, AssetName);
        var newExePath = exePath + ".new";
        var batchPath = Path.Combine(Path.GetDirectoryName(exePath)!, "update.bat");

        _logger.LogInformation("Downloading {Latest} from {Url}", latest, downloadUrl);

        byte[] bytes;
        try
        {
            bytes = await client.GetByteArrayAsync(downloadUrl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Download failed: {Message}", ex.Message);
            return;
        }

        await File.WriteAllBytesAsync(newExePath, bytes);

        await File.WriteAllTextAsync(batchPath,
            $"""
            @echo off
            timeout /t 2 /nobreak > nul
            move /y "{newExePath}" "{exePath}"
            start "" "{exePath}"
            del "%~f0"
            """);

        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{batchPath}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        });

        _logger.LogInformation("Update downloaded. Restarting to apply {Latest}...", latest);
        Environment.Exit(0);
    }
}
