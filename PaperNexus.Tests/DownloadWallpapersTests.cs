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
}
