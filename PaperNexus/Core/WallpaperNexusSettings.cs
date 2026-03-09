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

public enum SlideshowOrder
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
    public SlideshowScheduleMode ScheduleMode { get; set; } = SlideshowScheduleMode.IntervalMinutes;
    public int IntervalMinutes { get; set; } = 30;
    public int IntervalHours { get; set; } = 1;
    public string CronExpression { get; set; } = "*/30 * * * *";
    public SlideshowOrder Order { get; set; } = SlideshowOrder.NewestFirst;
    public WallpaperFillStyle FillStyle { get; set; } = WallpaperFillStyle.Fill;
    public bool FavoritePriorityEnabled { get; set; }
    public int FavoritePriorityWeight { get; set; } = 3;
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
    public string CronExpression { get; set; } = "0 */8 * * *";
    public DateTimeOffset? LastDownloadUtc { get; set; }

    private bool _isEnabled = true;

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }
}

public enum AnnotationPosition
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

public class AnnotationSettings
{
    public string FontFamily { get; set; } = BundledFonts.DefaultFontFamily;
    public int FontSize { get; set; } = 18;
    public string Color { get; set; } = "#F5F5F5";
    public AnnotationPosition Position { get; set; } = AnnotationPosition.TopLeft;
    public bool OutlineEnabled { get; set; } = true;
}

public class DownloadSettings
{
    public string Folder { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "PaperNexus");
    public int ResolutionWidth { get; set; } = 0;
    public int ResolutionHeight { get; set; } = 0;
    public int RetentionDays { get; set; } = 365;
}

public class WallpaperNexusSettings
{
    public static readonly string SettingsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PaperNexus", "settings.json");

    public SlideshowSettings Slideshow { get; set; } = new();
    public DownloadSettings Download { get; set; } = new();

    public string CurrentWallpaperPath { get; set; } = string.Empty;
    public bool AnnotateWallpaper { get; set; } = true;
    public AnnotationSettings Annotation { get; set; } = new();
    public bool RunOnStartup { get; set; } = true;
    public bool AutoUpdatesEnabled { get; set; } = true;
    public bool DebugMode { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public List<string> FavoriteWallpapers { get; set; } = [];
    public List<string> BannedWallpapers { get; set; } = [];

    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<WallpaperSource> Sources { get; set; } = DefaultSources;

    public double? WindowX { get; set; }
    public double? WindowY { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Download.Folder);

    public static List<WallpaperSource> DefaultSources =>
    [
        new() { Name = "Bing Daily 4k", Url = "https://peapix.com/bing/feed?country=us" },
        new() { Name = "Spotlight Daily 4k", Url = "https://peapix.com/spotlight/feed" },
    ];

    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        Formatting = Formatting.Indented,
        Converters = { new StringEnumConverter() },
    };

    // Reads settings.json and deserialises it; fills in defaults for any missing or
    // invalid fields introduced by schema migrations. Returns a fresh default instance
    // if the file does not yet exist or is corrupted.
    public static async Task<WallpaperNexusSettings> LoadAsync()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = await File.ReadAllTextAsync(SettingsFilePath);
                var settings = JsonConvert.DeserializeObject<WallpaperNexusSettings>(json, JsonSettings)
                    ?? new WallpaperNexusSettings();
                ApplyDefaults(settings);
                return settings;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load settings: {ex.Message}");
        }
        return new WallpaperNexusSettings();
    }

    // Guards against settings files written by older versions that omitted certain fields,
    // or that contain zero/empty values that would break the scheduler or file paths.
    private static void ApplyDefaults(WallpaperNexusSettings settings)
    {
        // Ensure slideshow sub-object exists and that its computed fields are valid
        var defaultSlideshow = new SlideshowSettings();
        settings.Slideshow ??= defaultSlideshow;
        if (string.IsNullOrWhiteSpace(settings.Slideshow.CronExpression))
            settings.Slideshow.CronExpression = defaultSlideshow.CronExpression;
        if (settings.Slideshow.IntervalMinutes <= 0)
            settings.Slideshow.IntervalMinutes = defaultSlideshow.IntervalMinutes;
        if (settings.Slideshow.IntervalHours <= 0)
            settings.Slideshow.IntervalHours = defaultSlideshow.IntervalHours;

        // Ensure download sub-object exists; RetentionDays of 0 would delete everything immediately
        var defaultDownload = new DownloadSettings();
        settings.Download ??= defaultDownload;
        if (string.IsNullOrWhiteSpace(settings.Download.Folder))
            settings.Download.Folder = defaultDownload.Folder;
        if (settings.Download.RetentionDays <= 0)
            settings.Download.RetentionDays = defaultDownload.RetentionDays;

        // Initialise reference-type properties that may be null after JSON deserialisation
        settings.CurrentWallpaperPath ??= string.Empty;
        settings.Annotation ??= new AnnotationSettings();
        settings.FavoriteWallpapers ??= [];
        settings.BannedWallpapers ??= [];
        // A weight of 0 or less would make favorite priority a no-op
        if (settings.Slideshow.FavoritePriorityWeight <= 0)
            settings.Slideshow.FavoritePriorityWeight = 3;

        // If sources list was cleared or never saved, restore the built-in defaults
        if (settings.Sources is null || settings.Sources.Count == 0)
            settings.Sources = DefaultSources;
    }

    public async Task SaveAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsFilePath));
        await File.WriteAllTextAsync(SettingsFilePath, JsonConvert.SerializeObject(this, JsonSettings));
    }
}
