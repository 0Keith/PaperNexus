using Cronos;
using PaperNexus.Core;
using System.Diagnostics;
using System.Text.Json;

namespace PaperNexus;

internal interface ICheckForUpdates
{
    Task CheckAsync(IProgress<string>? progress = null);
}

internal sealed class AutoUpdateService : ICheckForUpdates, IAddSingleton<ICheckForUpdates>
{
    private const string GitHubRepo = "0Keith/PaperNexus";
    private const string AssetName = "PaperNexus.exe";

    private readonly ILogger<AutoUpdateService> _logger;

    public AutoUpdateService(ILogger<AutoUpdateService> logger)
    {
        _logger = logger.ThrowIfNull();
    }

    public async Task CheckAsync(IProgress<string>? progress = null)
    {
        var current = typeof(AutoUpdateService).Assembly.GetName().Version;
        if (current is null)
            return;

        _logger.LogInformation("Checking for updates. Current version: {Version}", current);
        progress?.Report("Checking for updates...");

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
            throw;
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
        var backupPath = exePath + ".bak";
        var batchPath = Path.Combine(Path.GetDirectoryName(exePath)!, "update.bat");

        _logger.LogInformation("Downloading {Latest} from {Url}", latest, downloadUrl);
        progress?.Report($"Downloading {tag}...");

        byte[] bytes;
        try
        {
            bytes = await client.GetByteArrayAsync(downloadUrl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Download failed: {Message}", ex.Message);
            throw;
        }

        await File.WriteAllBytesAsync(newExePath, bytes);

        // Remove the Zone.Identifier alternate data stream so Smart App Control
        // does not treat the downloaded file as untrusted internet content.
        try
        {
            File.Delete(newExePath + ":Zone.Identifier");
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Zone.Identifier removal skipped: {Message}", ex.Message);
        }

        await File.WriteAllTextAsync(batchPath,
            $"""
            @echo off
            timeout /t 2 /nobreak > nul
            copy /y "{exePath}" "{backupPath}" > nul
            if errorlevel 1 exit /b 1
            move /y "{newExePath}" "{exePath}"
            if errorlevel 1 (
                del "{backupPath}" 2>nul
                exit /b 1
            )
            start "" "{exePath}" --updated
            timeout /t 8 /nobreak > nul
            tasklist /fi "imagename eq {AssetName}" /fo csv 2>nul | findstr /i "{Path.GetFileNameWithoutExtension(AssetName)}" > nul
            if errorlevel 1 (
                copy /y "{backupPath}" "{exePath}" > nul
                start "" "{exePath}"
            )
            del "{backupPath}" 2>nul
            del "%~f0"
            """);

        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{batchPath}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        });

        _logger.LogInformation("Update downloaded. Restarting to apply {Latest}...", latest);
        progress?.Report("Restarting...");
        await Task.Delay(500); // brief pause so the UI can display "Restarting..." before exit
        Environment.Exit(0);
    }
}

internal sealed class AutoUpdateJob : IScheduleScopedJob
{
    private readonly ICheckForUpdates _checkForUpdates;

    public AutoUpdateJob(ICheckForUpdates checkForUpdates)
    {
        _checkForUpdates = checkForUpdates.ThrowIfNull();
    }

    public Task<JobConfig> GetJobConfigAsync() =>
        Task.FromResult(new JobConfig(
            CronExpression: CronExpression.Parse("0 3 * * *"),
            ExecuteOnStartup: true));

    public Task ExecuteAsync() => _checkForUpdates.CheckAsync(progress: null);
}
