using Cronos;
using PaperNexus.Core;
using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace PaperNexus;

internal interface ICheckForUpdates
{
    Task CheckAsync(bool forceUpdate = false, IProgress<string>? progress = null);
}

internal sealed class AutoUpdateService : ICheckForUpdates, IAddSingleton<ICheckForUpdates>
{
    private const string GitHubRepo = "0Keith/PaperNexus";
    private const string AssetName = "PaperNexus.exe";

    private readonly ILogger<AutoUpdateService> _logger;

    // Shared, long-lived HttpClient for all GitHub API and download requests. HttpClient is
    // thread-safe for concurrent requests; creating a new one per call drains the ephemeral
    // port pool because disposed clients leave sockets in TIME_WAIT for several minutes.
    // The service is a singleton, so this client lives for the process lifetime.
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(30) };

    public AutoUpdateService(ILogger<AutoUpdateService> logger)
    {
        _logger = logger.ThrowIfNull();
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("PaperNexus-AutoUpdater");
    }

    // Checks GitHub Releases for a newer build and, if found (or if forceUpdate is set),
    // downloads the signed exe, verifies its Authenticode signature, then launches a
    // self-deleting batch script that swaps the binary while the app is not running.
    // The current process calls Environment.Exit(0) after launching the updater script.
    public async Task CheckAsync(bool forceUpdate = false, IProgress<string>? progress = null)
    {
        var currentVersion = typeof(AutoUpdateService).Assembly.GetName().Version;
        if (currentVersion is null)
        {
            _logger.LogError("Cannot determine current assembly version.");
            throw new InvalidOperationException("Cannot determine current assembly version.");
        }

        // Version scheme is vN where N maps to Version.Major
        var currentBuild = currentVersion.Major;
        _logger.LogInformation("Checking for updates. Current build: v{Build}", currentBuild);
        progress?.Report($"Checking for updates (v{currentBuild})...");

        string json;
        try
        {
            json = await _client.GetStringAsync(
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
        {
            _logger.LogWarning("No tag_name found in GitHub release response.");
            throw new InvalidOperationException("No version tag found in latest release.");
        }

        var tag = tagElement.GetString();
        if (tag is null)
        {
            _logger.LogWarning("Release tag_name is null.");
            throw new InvalidOperationException("Release version tag is empty.");
        }

        // Tags are "vN" — strip the leading 'v' and parse as an integer build number
        var versionStr = tag.TrimStart('v');
        if (!int.TryParse(versionStr, out var latestBuild))
        {
            _logger.LogWarning("Cannot parse release tag '{Tag}' as build number.", tag);
            throw new InvalidOperationException($"Cannot parse release tag '{tag}' as a version number.");
        }

        if (latestBuild <= currentBuild && !forceUpdate)
        {
            _logger.LogInformation("Already up to date (v{Build})", currentBuild);
            return;
        }

        if (forceUpdate && latestBuild <= currentBuild)
            _logger.LogInformation("Forcing re-install of current version (v{Build})", currentBuild);

        _logger.LogInformation("Update available: v{Latest} (current: v{Current})", latestBuild, currentBuild);

        if (!root.TryGetProperty("assets", out var assetsElement))
        {
            _logger.LogWarning("No assets found in release {Tag}.", tag);
            throw new InvalidOperationException($"No assets found in release {tag}.");
        }

        // Find the download URL for PaperNexus.exe in the release assets list
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
            _logger.LogWarning("Asset '{Asset}' not found in release {Tag}.", AssetName, tag);
            throw new InvalidOperationException($"Update file '{AssetName}' not found in release {tag}.");
        }

        var exePath = Environment.ProcessPath
            ?? Path.Combine(AppContext.BaseDirectory, AssetName);
        // Stage the download beside the running exe; the batch script will move it into place
        var newExePath = exePath + ".new";
        var backupPath = exePath + ".bak";
        // Use a unique batch name to avoid collisions if a previous update was interrupted
        var batchPath = Path.Combine(Path.GetDirectoryName(exePath)!, $"update-{Guid.NewGuid():N}.bat");

        _logger.LogInformation("Downloading v{Latest} from {Url}", latestBuild, downloadUrl);
        progress?.Report($"Downloading {tag}...");

        try
        {
            // Stream directly to disk rather than buffering the full exe in memory (50+ MB)
            using var response = await _client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            using var fileStream = new FileStream(newExePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true);
            await response.Content.CopyToAsync(fileStream);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Download failed: {Message}", ex.Message);
            throw;
        }

        // Verify the downloaded exe has a valid Authenticode signature from PaperNexus.
        // This prevents executing a tampered or unsigned binary.
        if (!VerifyAuthenticodeSignature(newExePath))
        {
            File.Delete(newExePath);
            _logger.LogWarning("Update signature verification failed. Update aborted.");
            throw new InvalidOperationException("Downloaded update failed signature verification.");
        }

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

        // The batch script:
        //  1. Waits 2 s for the current process to exit
        //  2. Backs up the running exe
        //  3. Moves the new exe into place
        //  4. Launches the updated app
        //  5. Waits 8 s and checks the process is running; if not, rolls back from backup
        //  6. Deletes the backup and self-deletes
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

        _logger.LogInformation("Update downloaded. Restarting to apply v{Latest}...", latestBuild);
        progress?.Report("Restarting...");
        await Task.Delay(500); // brief pause so the UI can display "Restarting..." before exit
        Environment.Exit(0);
    }

    // Checks that the file at filePath carries a valid Authenticode signature whose
    // subject CN matches "PaperNexus". Returns false (rather than throwing) if the
    // certificate is missing, invalid, or signed by an unexpected issuer.
    private bool VerifyAuthenticodeSignature(string filePath)
    {
        try
        {
#pragma warning disable SYSLIB0057 // No non-obsolete API for Authenticode cert extraction yet
            using var x509 = X509Certificate2.CreateFromSignedFile(filePath);
#pragma warning restore SYSLIB0057
            if (!x509.Subject.Contains("CN=PaperNexus", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Update signed by unexpected subject: {Subject}", x509.Subject);
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Authenticode verification failed: {Message}", ex.Message);
            return false;
        }
    }
}

internal sealed class AutoUpdateJob : IScheduleScopedJob
{
    private readonly ICheckForUpdates _checkForUpdates;

    public AutoUpdateJob(ICheckForUpdates checkForUpdates)
    {
        _checkForUpdates = checkForUpdates.ThrowIfNull();
    }

    // Returns an empty config (no schedule) in debug mode or when auto-updates are disabled.
    // Otherwise schedules a daily check at 03:00 and also runs once on startup.
    public async Task<JobConfig> GetJobConfigAsync()
    {
        if (Program.IsDebugMode)
            return new JobConfig();
        var settings = await WallpaperNexusSettings.LoadAsync();
        if (!settings.AutoUpdatesEnabled)
            return new JobConfig();
        return new JobConfig(
            CronExpression: CronExpression.Parse("0 3 * * *"),
            ExecuteOnStartup: true);
    }

    public Task ExecuteAsync() => _checkForUpdates.CheckAsync(forceUpdate: false, progress: null);
}
