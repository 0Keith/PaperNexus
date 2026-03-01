using Newtonsoft.Json;
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
    Oldest,
    Newest,
    Never,
}

public enum SwitchScheduleMode
{
    CronExpression,
    IntervalMinutes,
    IntervalHours,
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
    public SwitchScheduleMode SwitchScheduleMode { get; set; } = SwitchScheduleMode.CronExpression;
    public int SwitchIntervalMinutes { get; set; } = 30;
    public int SwitchIntervalHours { get; set; } = 1;
    public string SwitchCronExpression { get; set; } = "*/30 * * * *";

    public int ImageWidth { get; set; } = 0;
    public int ImageHeight { get; set; } = 0;
    public int RetentionDays { get; set; } = 365;
    public string CurrentWallpaperPath { get; set; } = string.Empty;
    public WallpaperFillStyle FillStyle { get; set; } = WallpaperFillStyle.Fill;
    public WallpaperSwitchPattern SwitchPattern { get; set; } = WallpaperSwitchPattern.Newest;
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

    public static async Task<WallpaperNexusSettings> LoadAsync()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = await File.ReadAllTextAsync(SettingsFilePath);
                var settings = JsonConvert.DeserializeObject<WallpaperNexusSettings>(json) ?? new WallpaperNexusSettings();
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
        await File.WriteAllTextAsync(SettingsFilePath, JsonConvert.SerializeObject(this, Formatting.Indented));
    }
}
