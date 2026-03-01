using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace PaperNexus.Core;

public enum WallpaperFillStyle
{
    Fill,
    Fit,
    Stretch,
    Tile,
    Center,
    Span,
}

public enum WallpaperSwitchPattern
{
    Alphabetical,
    Random,
    OldestFirst,
    NewestFirst,
    Never,
}

public enum SlideshowScheduleMode
{
    CronExpression,
    IntervalMinutes,
    IntervalHours,
}

public class SlideshowSettings
{
    public SlideshowScheduleMode ScheduleMode { get; set; } = SlideshowScheduleMode.CronExpression;
    public int IntervalMinutes { get; set; } = 30;
    public int IntervalHours { get; set; } = 1;
    public string CronExpression { get; set; } = "*/30 * * * *";
    public WallpaperSwitchPattern Pattern { get; set; } = WallpaperSwitchPattern.NewestFirst;
}

public class WallpaperSource
{
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string CronExpression { get; set; } = "0 * * * *";
    public bool IsEnabled { get; set; } = true;
}

public class WallpaperNexusSettings
{
    public static readonly string SettingsFilePath = Path.Combine(
        AppContext.BaseDirectory, "settings.json");

    public string WallpapersFolder { get; set; } = string.Empty;
    public SlideshowSettings Slideshow { get; set; } = new();

    public int ResolutionWidth { get; set; } = 0;
    public int ResolutionHeight { get; set; } = 0;
    public int RetentionDays { get; set; } = 365;
    public string CurrentWallpaperPath { get; set; } = string.Empty;
    public WallpaperFillStyle FillStyle { get; set; } = WallpaperFillStyle.Fill;
    public bool AnnotateWallpaper { get; set; } = true;
    public bool RunOnStartup { get; set; } = true;

    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<WallpaperSource> Sources { get; set; } = new()
    {
        new WallpaperSource { Name = "Bing Daily", Url = "https://peapix.com/bing/feed?country=us" }
    };

    public double? WindowX { get; set; }
    public double? WindowY { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(WallpapersFolder);

    public static readonly WallpaperSource DefaultBingSource = new()
    {
        Name = "Bing Daily",
        Url = "https://peapix.com/bing/feed?country=us"
    };

    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        Formatting = Formatting.Indented,
        Converters = { new StringEnumConverter() },
    };

    public static async Task<WallpaperNexusSettings> LoadAsync()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = await File.ReadAllTextAsync(SettingsFilePath);
                var settings = JsonConvert.DeserializeObject<WallpaperNexusSettings>(json, JsonSettings) ?? new WallpaperNexusSettings();
                if (settings.Sources.Count == 0)
                    settings.Sources.Add(new WallpaperSource { Name = DefaultBingSource.Name, Url = DefaultBingSource.Url });
                return settings;
            }
        }
        catch { }
        return new WallpaperNexusSettings();
    }

    public async Task SaveAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsFilePath));
        await File.WriteAllTextAsync(SettingsFilePath, JsonConvert.SerializeObject(this, JsonSettings));
    }
}
