using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace PaperNexus.Tests;

[Collection("Wallpaper")]
public class SwitchWallpaperTests : IAsyncLifetime, IDisposable
{
    private readonly string _wallpaperDir;

    public SwitchWallpaperTests()
    {
        _wallpaperDir = Path.Combine(Path.GetTempPath(), $"PaperNexus_Test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_wallpaperDir);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        TestHelpers.Cleanup();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_wallpaperDir)) Directory.Delete(_wallpaperDir, true); }
        catch { }
    }

    [Fact]
    public async Task SwitchToNext_DeletesStaleJpg()
    {
        // Arrange: one small wallpaper + a stale current.jpg
        var wallpaperPath = Path.Combine(_wallpaperDir, "test-wallpaper.png");
        TestHelpers.CreateSmallPng(wallpaperPath);
        File.WriteAllBytes(TestHelpers.JpgPath, [0xFF, 0xD8, 0xFF]); // fake JPEG marker
        await TestHelpers.WriteSettingsAsync(_wallpaperDir);

        var switcher = new SwitchWallpaper(NullLogger<SwitchWallpaper>.Instance);

        // Act
        var result = await switcher.SwitchToNextAsync();

        // Assert
        Assert.NotNull(result);
        Assert.True(File.Exists(TestHelpers.PngPath), "current.png should exist after switch");
        Assert.False(File.Exists(TestHelpers.JpgPath), "stale current.jpg should be deleted");
    }

    [Fact]
    public async Task SwitchToNext_DeletesStalePng_WhenJpegFallback()
    {
        // Arrange: one oversized wallpaper + a stale current.png
        var wallpaperPath = Path.Combine(_wallpaperDir, "huge-wallpaper.png");
        TestHelpers.CreateOversizedPng(wallpaperPath);
        File.WriteAllBytes(TestHelpers.PngPath, [0x89, 0x50, 0x4E, 0x47]); // fake PNG marker
        await TestHelpers.WriteSettingsAsync(_wallpaperDir);

        var switcher = new SwitchWallpaper(NullLogger<SwitchWallpaper>.Instance);

        // Act
        var result = await switcher.SwitchToNextAsync();

        // Assert
        Assert.NotNull(result);
        Assert.True(File.Exists(TestHelpers.JpgPath), "current.jpg should exist after JPEG fallback");
        Assert.False(File.Exists(TestHelpers.PngPath), "stale current.png should be deleted");
    }

    [Fact]
    public async Task SwitchToNext_NoCounterpart_DoesNotThrow()
    {
        // Arrange: one wallpaper, no pre-existing current files
        var wallpaperPath = Path.Combine(_wallpaperDir, "test-wallpaper.png");
        TestHelpers.CreateSmallPng(wallpaperPath);
        TestHelpers.Cleanup(); // ensure no current.png/jpg
        await TestHelpers.WriteSettingsAsync(_wallpaperDir);

        var switcher = new SwitchWallpaper(NullLogger<SwitchWallpaper>.Instance);

        // Act
        var result = await switcher.SwitchToNextAsync();

        // Assert
        Assert.NotNull(result);
        var pngExists = File.Exists(TestHelpers.PngPath);
        var jpgExists = File.Exists(TestHelpers.JpgPath);
        Assert.True(pngExists || jpgExists, "At least one current file should exist");
        Assert.False(pngExists && jpgExists, "Only one format should exist at a time");
    }
}
