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
}

public enum SlideshowScheduleMode
{
    CronExpression,
    IntervalMinutes,
    IntervalHours,
}

public class SlideshowSettings
{
    public bool Enabled { get; set; } = true;
    public SlideshowScheduleMode ScheduleMode { get; set; } = SlideshowScheduleMode.CronExpression;
    public int IntervalMinutes { get; set; } = 30;
    public int IntervalHours { get; set; } = 1;
    public string CronExpression { get; set; } = "*/30 * * * *";
    public WallpaperSwitchPattern Pattern { get; set; } = WallpaperSwitchPattern.NewestFirst;
    public WallpaperFillStyle FillStyle { get; set; } = WallpaperFillStyle.Fill;
}

public enum WallpaperSourceType
{
    HttpJson,
}

public class WallpaperSource
{
    public string Name { get; set; } = string.Empty;
    public WallpaperSourceType Type { get; set; } = WallpaperSourceType.HttpJson;
    public string Url { get; set; } = string.Empty;
    public string ImageUrlJPath { get; set; } = "$[*].imageUrl";
    public string TitleJPath { get; set; } = "$[*].title";
    public string CronExpression { get; set; } = "0 * * * *";
    public bool IsEnabled { get; set; } = true;
}

public class DownloadSettings
{
    public string Folder { get; set; } = string.Empty;
    public int ResolutionWidth { get; set; } = 0;
    public int ResolutionHeight { get; set; } = 0;
    public int RetentionDays { get; set; } = 365;
}

public class WallpaperNexusSettings
{
    public static readonly string SettingsFilePath = Path.Combine(
        AppContext.BaseDirectory, "settings.json");

    public SlideshowSettings Slideshow { get; set; } = new();
    public DownloadSettings Download { get; set; } = new();

    public string CurrentWallpaperPath { get; set; } = string.Empty;
    public bool AnnotateWallpaper { get; set; } = true;
    public bool RunOnStartup { get; set; } = true;

    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<WallpaperSource> Sources { get; set; } = new()
    {
        new WallpaperSource { Name = "Peapix Bing Daily 4k", Url = "https://peapix.com/bing/feed?country=us" }
    };

    public double? WindowX { get; set; }
    public double? WindowY { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Download.Folder);

    public static readonly WallpaperSource DefaultBingSource = new()
    {
        Name = "Peapix Bing Daily 4k",
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
