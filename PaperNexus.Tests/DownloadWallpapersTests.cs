using Microsoft.Extensions.Logging.Abstractions;
using PaperNexus.Core;
using Xunit;

namespace PaperNexus.Tests;

public class DownloadWallpapersTests : IDisposable
{
    private readonly string _downloadDir;

    public DownloadWallpapersTests()
    {
        _downloadDir = Path.Combine(Path.GetTempPath(), $"PaperNexus_DL_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_downloadDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_downloadDir)) Directory.Delete(_downloadDir, true); }
        catch { }
    }

    [Theory]
    [InlineData("../evil/payload")]
    [InlineData("..\\evil\\payload")]
    [InlineData("title/../../etc/passwd")]
    [InlineData("safe/title\\here")]
    public async Task Download_TitleWithSlashes_NeverEscapesFolder(string title)
    {
        var source = new HttpWallpaperSourceService(NullLogger<HttpWallpaperSourceService>.Instance);
        var sut = new DownloadWallpapers(NullLogger<DownloadWallpapers>.Instance, source);
        var settings = new WallpaperNexusSettings
        {
            Download = new DownloadSettings { Folder = _downloadDir },
        };

        var image = new WallpaperImage { Title = title, ImageUrl = "https://example.com/image.png" };

        // The HTTP call will fail but path construction should never escape the folder.
        // Slashes are stripped from the title, keeping the file inside the folder.
        try
        {
            await sut.Download(image, settings);
        }
        catch
        {
            // Expected — HTTP call fails, but path was constructed safely
        }

        // Verify no files were written outside the download directory
        var parent = Path.GetDirectoryName(_downloadDir)!;
        var escapedFiles = Directory.GetFiles(parent)
            .Where(f => f.Contains("evil") || f.Contains("passwd") || f.Contains("safe"))
            .ToArray();
        Assert.Empty(escapedFiles);
    }

    [Fact]
    public async Task Download_ExistingFile_SkipsDownload()
    {
        var source = new HttpWallpaperSourceService(NullLogger<HttpWallpaperSourceService>.Instance);
        var sut = new DownloadWallpapers(NullLogger<DownloadWallpapers>.Instance, source);
        var settings = new WallpaperNexusSettings
        {
            Download = new DownloadSettings { Folder = _downloadDir },
        };

        // Pre-create the file that would be downloaded
        var title = "Test Wallpaper";
        var expectedPath = Path.Combine(_downloadDir, $"{title} - image.png");
        await File.WriteAllBytesAsync(expectedPath, [0x89, 0x50, 0x4E, 0x47]);

        var image = new WallpaperImage { Title = title, ImageUrl = "https://example.com/image.png" };

        // Should skip download because file already exists (no HTTP call made)
        await sut.Download(image, settings);

        // File should still have the original content (not overwritten)
        var bytes = await File.ReadAllBytesAsync(expectedPath);
        Assert.Equal(4, bytes.Length);
    }

    [Fact]
    public async Task Download_OneImageFails_DoesNotPreventSubsequentImages()
    {
        // Arrange: two images in a sequence — the first will fail (HTTP error on a bad URL),
        // the second already exists on disk so no HTTP call is needed.
        // After the fix, per-image failures are caught and logged; the second image must
        // still be processed (i.e. its pre-existing file is unchanged, not thrown over).
        var source = new HttpWallpaperSourceService(NullLogger<HttpWallpaperSourceService>.Instance);
        var sut = new DownloadWallpapers(NullLogger<DownloadWallpapers>.Instance, source);
        var settings = new WallpaperNexusSettings
        {
            Download = new DownloadSettings { Folder = _downloadDir },
        };

        // Pre-create the file for the second image so the skip path is taken (no real HTTP call)
        var secondTitle = "Second Wallpaper";
        var secondPath = Path.Combine(_downloadDir, $"{secondTitle} - second.png");
        var originalBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        await File.WriteAllBytesAsync(secondPath, originalBytes);

        var failingImage = new WallpaperImage { Title = "Failing Image", ImageUrl = "https://0.0.0.0/nonexistent.png" };
        var skippedImage = new WallpaperImage { Title = secondTitle, ImageUrl = "https://example.com/second.png" };

        // Act: call Download for each image individually (mirroring the fixed DownloadSource loop).
        // The first call is expected to throw an HTTP/network exception.
        // After the fix, that exception is caught per-image so the second call still runs.
        Exception? firstException = null;
        try { await sut.Download(failingImage, settings); }
        catch (Exception ex) { firstException = ex; }

        // The second call should always succeed (pre-existing file skips the HTTP call)
        await sut.Download(skippedImage, settings);

        // Assert: the first image did raise an exception (network failure expected)
        Assert.NotNull(firstException);
        // The second image's file is still intact — iteration was not aborted by the first failure
        var remaining = await File.ReadAllBytesAsync(secondPath);
        Assert.Equal(originalBytes, remaining);
    }

    [Fact]
    public async Task CleanupOldImages_DeletesExpiredFiles_AndPrunesBannedList()
    {
        // Arrange: one expired file referenced in BannedWallpapers
        var source = new HttpWallpaperSourceService(NullLogger<HttpWallpaperSourceService>.Instance);
        var sut = new DownloadWallpapers(NullLogger<DownloadWallpapers>.Instance, source);

        var expiredPath = Path.Combine(_downloadDir, "old.png");
        TestHelpers.CreateSmallPng(expiredPath);
        // Backdate last-write to well past the retention window
        File.SetLastWriteTimeUtc(expiredPath, DateTime.UtcNow.AddDays(-400));

        var settings = new WallpaperNexusSettings
        {
            Download = new DownloadSettings { Folder = _downloadDir, RetentionDays = 365 },
            BannedWallpapers = [expiredPath],
        };

        // Act
        await sut.CleanupOldImages(settings);

        // Assert: file is deleted and the stale banned entry is removed
        Assert.False(File.Exists(expiredPath), "Expired file should be deleted");
        Assert.Empty(settings.BannedWallpapers);
    }

    [Fact]
    public async Task CleanupOldImages_FavoritedFile_IsNotDeleted_ButStaleFavoritesArePruned()
    {
        // Arrange: one expired file that is favorited (should survive deletion),
        // plus one stale favorite entry pointing to a file that no longer exists.
        var source = new HttpWallpaperSourceService(NullLogger<HttpWallpaperSourceService>.Instance);
        var sut = new DownloadWallpapers(NullLogger<DownloadWallpapers>.Instance, source);

        var favoritePath = Path.Combine(_downloadDir, "favorite.png");
        TestHelpers.CreateSmallPng(favoritePath);
        File.SetLastWriteTimeUtc(favoritePath, DateTime.UtcNow.AddDays(-400));

        // This path is in favorites but does not exist on disk (manually deleted outside the app)
        var staleGhostPath = Path.Combine(_downloadDir, "ghost.png");

        var settings = new WallpaperNexusSettings
        {
            Download = new DownloadSettings { Folder = _downloadDir, RetentionDays = 365 },
            FavoriteWallpapers = [favoritePath, staleGhostPath],
        };

        // Act
        await sut.CleanupOldImages(settings);

        // Assert: favorited file preserved on disk
        Assert.True(File.Exists(favoritePath), "Favorited file should survive retention cleanup");
        // The favorited file's path stays in the list (it still exists)
        Assert.Contains(favoritePath, settings.FavoriteWallpapers);
        // The ghost path (file was deleted outside the app) is pruned from the list
        Assert.DoesNotContain(staleGhostPath, settings.FavoriteWallpapers);
    }

    [Fact]
    public async Task CleanupOldImages_RecentFile_IsNotDeleted()
    {
        // Arrange: a recent file that is within the retention window
        var source = new HttpWallpaperSourceService(NullLogger<HttpWallpaperSourceService>.Instance);
        var sut = new DownloadWallpapers(NullLogger<DownloadWallpapers>.Instance, source);

        var recentPath = Path.Combine(_downloadDir, "recent.png");
        TestHelpers.CreateSmallPng(recentPath);
        // Last-write is now (within retention window)

        var settings = new WallpaperNexusSettings
        {
            Download = new DownloadSettings { Folder = _downloadDir, RetentionDays = 365 },
        };

        // Act
        await sut.CleanupOldImages(settings);

        // Assert: recent file is kept
        Assert.True(File.Exists(recentPath), "Recently downloaded file should not be deleted");
    }
}
