using CommunityToolkit.Mvvm.ComponentModel;
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

public class WallpaperSource : ObservableObject
{
    public string Name { get; set; } = string.Empty;
    public WallpaperSourceType Type { get; set; } = WallpaperSourceType.HttpJson;
    public string Url { get; set; } = string.Empty;
    public string ImageUrlJPath { get; set; } = "$[*].imageUrl";
    public string TitleJPath { get; set; } = "$[*].title";
    public string CronExpression { get; set; } = "0 * * * *";

    private bool _isEnabled = true;

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }
}

public class DownloadSettings
{
    public string Folder { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "wallpapers");
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
    public bool AutoUpdatesEnabled { get; set; } = true;

    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<WallpaperSource> Sources { get; set; } = new()
    {
        new WallpaperSource { Name = "Bing Daily 4k -United States", Url = "https://peapix.com/bing/feed?country=us" },
        new WallpaperSource { Name = "Bing Daily 4k -Australia", Url = "https://peapix.com/bing/feed?country=au" },
        new WallpaperSource { Name = "Bing Daily 4k -Brazil", Url = "https://peapix.com/bing/feed?country=br" },
        new WallpaperSource { Name = "Bing Daily 4k -Canada", Url = "https://peapix.com/bing/feed?country=ca" },
        new WallpaperSource { Name = "Bing Daily 4k -China", Url = "https://peapix.com/bing/feed?country=cn" },
        new WallpaperSource { Name = "Bing Daily 4k -France", Url = "https://peapix.com/bing/feed?country=fr" },
        new WallpaperSource { Name = "Bing Daily 4k -Germany", Url = "https://peapix.com/bing/feed?country=de" },
        new WallpaperSource { Name = "Bing Daily 4k -India", Url = "https://peapix.com/bing/feed?country=in" },
        new WallpaperSource { Name = "Bing Daily 4k -Italy", Url = "https://peapix.com/bing/feed?country=it" },
        new WallpaperSource { Name = "Bing Daily 4k -Japan", Url = "https://peapix.com/bing/feed?country=jp" },
        new WallpaperSource { Name = "Bing Daily 4k -Spain", Url = "https://peapix.com/bing/feed?country=es" },
        new WallpaperSource { Name = "Bing Daily 4k -United Kingdom", Url = "https://peapix.com/bing/feed?country=gb" },
    };

    public double? WindowX { get; set; }
    public double? WindowY { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Download.Folder);

    public static readonly WallpaperSource DefaultBingSource = new()
    {
        Name = "Bing Daily 4k -United States",
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
