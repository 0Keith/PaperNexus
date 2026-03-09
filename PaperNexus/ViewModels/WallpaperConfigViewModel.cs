using System.Collections.ObjectModel;
using Avalonia;
using Cronos;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PaperNexus.Core;
using BundledFonts = PaperNexus.Core.BundledFonts;

namespace PaperNexus.ViewModels;

public record ResolutionOption(string Label, string Description, int Width, int Height)
{
    public override string ToString() => Label;
}

public record FillStyleOption(string Label, string Description, WallpaperFillStyle Style)
{
    public override string ToString() => Label;
}

public record SlideshowOrderOption(string Label, string Description, SlideshowOrder Order)
{
    public override string ToString() => Label;
}

public record AnnotationPositionOption(string Label, string Description, AnnotationPosition Position)
{
    public override string ToString() => Label;
}

public partial class WallpaperConfigViewModel : ObservableObject
{
    public static readonly IReadOnlyList<ResolutionOption> ResolutionOptions = new[]
    {
        new ResolutionOption("Native",                 "Use the image's original resolution, no resampling",              0,    0),
        new ResolutionOption("HD (1280×720)",          "Low resolution, good for slow connections or limited storage", 1280,  720),
        new ResolutionOption("Full HD (1920×1080)",    "Standard HD, suitable for most 1080p displays",               1920, 1080),
        new ResolutionOption("2K (2560×1440)",         "Quad HD, ideal for 1440p displays",                           2560, 1440),
        new ResolutionOption("4K (3840×2160)",         "Ultra HD, best for large 4K displays",                        3840, 2160),
        new ResolutionOption("5K (5120×2880)",         "5K, for high-DPI displays and Apple Studio Display",          5120, 2880),
        new ResolutionOption("8K (7680×4320)",         "8K, maximum quality, larger file sizes",                      7680, 4320),
    };

    private readonly ISwitchWallpaper? _switchWallpaper;
    private readonly ICheckForUpdates? _checkForUpdates;
    private readonly IDownloadWallpapers? _downloadWallpapers;

    public static readonly IReadOnlyList<FillStyleOption> FillStyleOptions = new[]
    {
        new FillStyleOption("Fill",    "Crop and scale to fill the screen, maintaining aspect ratio",         WallpaperFillStyle.Fill),
        new FillStyleOption("Fit",     "Scale to fit within the screen, adding black bars as needed",          WallpaperFillStyle.Fit),
        new FillStyleOption("Stretch", "Stretch to fill the screen, ignoring aspect ratio",                    WallpaperFillStyle.Stretch),
        new FillStyleOption("Tile",    "Repeat the image in a grid pattern to fill the screen",                WallpaperFillStyle.Tile),
        new FillStyleOption("Center",  "Display at original size, centered, no scaling applied",              WallpaperFillStyle.Center),
        new FillStyleOption("Span",    "Stretch a single image across all monitors as one continuous display", WallpaperFillStyle.Span),
    };

    public static readonly IReadOnlyList<SlideshowOrderOption> SlideshowOrderOptions = new[]
    {
        new SlideshowOrderOption("Alphabetical", "Cycle through wallpapers in A→Z order by filename",                   SlideshowOrder.Alphabetical),
        new SlideshowOrderOption("Oldest first", "Cycle from oldest to newest by file date",                            SlideshowOrder.OldestFirst),
        new SlideshowOrderOption("Newest first", "Cycle from newest to oldest by file date",                            SlideshowOrder.NewestFirst),
        new SlideshowOrderOption("Random",       "Pick a random wallpaper each time, favoring favorites if prioritized", SlideshowOrder.Random),
    };

    public static readonly IReadOnlyList<AnnotationPositionOption> AnnotationPositionOptions = new[]
    {
        new AnnotationPositionOption("Top Left",     "Show annotation in the top-left corner of the wallpaper",     AnnotationPosition.TopLeft),
        new AnnotationPositionOption("Top Right",    "Show annotation in the top-right corner of the wallpaper",    AnnotationPosition.TopRight),
        new AnnotationPositionOption("Bottom Left",  "Show annotation in the bottom-left corner of the wallpaper",  AnnotationPosition.BottomLeft),
        new AnnotationPositionOption("Bottom Right", "Show annotation in the bottom-right corner of the wallpaper", AnnotationPosition.BottomRight),
    };

    public static readonly IReadOnlyList<string> FontFamilyOptions = BuildFontFamilyOptions();

    // Builds the font picker list: bundled fonts first, then curated system fonts
    // that are actually installed on the current machine (system fonts vary by OS/locale).
    private static List<string> BuildFontFamilyOptions()
    {
        var fonts = new List<string>(BundledFonts.Names);
        string[] commonSystemFonts = [
            "MS Gothic", "Arial", "Segoe UI", "Consolas",
            "Georgia", "Times New Roman", "Verdana", "Tahoma",
            "Courier New", "Impact", "Comic Sans MS",
        ];
        foreach (var name in commonSystemFonts)
        {
            if (SixLabors.Fonts.SystemFonts.TryGet(name, out _))
                fonts.Add(name);
        }
        return fonts;
    }

    [ObservableProperty]
    private string _folder;

    [ObservableProperty]
    private string _slideshowCronExpression;

    [ObservableProperty]
    private int _slideshowIntervalMinutes;

    [ObservableProperty]
    private int _slideshowIntervalHours;

    private SlideshowScheduleMode _slideshowScheduleMode;

    // Notifies the derived boolean mode properties so XAML radio buttons re-evaluate,
    // then persists the change immediately.
    public SlideshowScheduleMode SlideshowScheduleMode
    {
        get => _slideshowScheduleMode;
        set
        {
            if (SetProperty(ref _slideshowScheduleMode, value))
            {
                OnPropertyChanged(nameof(IsIntervalMinutesMode));
                OnPropertyChanged(nameof(IsIntervalHoursMode));
                OnPropertyChanged(nameof(IsCronMode));
                TriggerSave();
            }
        }
    }

    public bool IsIntervalMinutesMode
    {
        get => _slideshowScheduleMode == SlideshowScheduleMode.IntervalMinutes;
        set { if (value) SlideshowScheduleMode = SlideshowScheduleMode.IntervalMinutes; }
    }

    public bool IsIntervalHoursMode
    {
        get => _slideshowScheduleMode == SlideshowScheduleMode.IntervalHours;
        set { if (value) SlideshowScheduleMode = SlideshowScheduleMode.IntervalHours; }
    }

    public bool IsCronMode
    {
        get => _slideshowScheduleMode == SlideshowScheduleMode.CronExpression;
        set { if (value) SlideshowScheduleMode = SlideshowScheduleMode.CronExpression; }
    }

    [ObservableProperty]
    private ResolutionOption _selectedResolution;

    [ObservableProperty]
    private FillStyleOption _selectedFillStyle;

    [ObservableProperty]
    private SlideshowOrderOption _selectedSlideshowOrder;

    [ObservableProperty]
    private int? _retentionDays;

    [ObservableProperty]
    private bool _annotateWallpaper = true;

    [ObservableProperty]
    private string _annotationFontFamily = BundledFonts.DefaultFontFamily;

    [ObservableProperty]
    private int _annotationFontSize = 18;

    [ObservableProperty]
    private string _annotationColor = "#F5F5F5";

    [ObservableProperty]
    private bool _annotationOutlineEnabled = true;

    [ObservableProperty]
    private AnnotationPositionOption _selectedAnnotationPosition;

    [ObservableProperty]
    private bool _runOnStartup = true;

    [ObservableProperty]
    private bool _autoUpdatesEnabled = true;

    [ObservableProperty]
    private bool _slideshowEnabled = true;

    [ObservableProperty]
    private bool _debugMode;

    [ObservableProperty]
    private bool _minimizeToTray = true;

    [ObservableProperty]
    private bool _favoritePriorityEnabled;

    [ObservableProperty]
    private int _favoritePriorityWeight = 3;

    [ObservableProperty]
    private ObservableCollection<GalleryItem> _galleryItems = [];

    [ObservableProperty]
    private string _statusMessage;

    [ObservableProperty]
    private IBrush _statusForeground;

    [ObservableProperty]
    private string _currentWallpaperPath;

    [ObservableProperty]
    private string _currentWallpaperName;

    [ObservableProperty]
    private ObservableCollection<WallpaperSource> _sources = [];

    [ObservableProperty]
    private WallpaperSource? _selectedSource;

    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _previewImage;

    [ObservableProperty]
    private bool _isCurrentWallpaperFavorited;

    [ObservableProperty]
    private ObservableCollection<string> _favoriteWallpapers = [];

    [ObservableProperty]
    private string? _selectedFavorite;

    private bool _isLoading;
    private CancellationTokenSource _statusCts = new();
    private CancellationTokenSource _galleryCts = new();


    // Initialises all observable properties to sensible defaults and resolves
    // background services from the App DI container. The WallpaperChanged event is
    // subscribed here so the preview updates automatically when the slideshow switches.
    public WallpaperConfigViewModel()
    {
        _folder = string.Empty;
        _slideshowCronExpression = string.Empty;
        _slideshowIntervalMinutes = 30;
        _slideshowIntervalHours = 1;
        _slideshowScheduleMode = SlideshowScheduleMode.CronExpression;
        _statusMessage = string.Empty;
        _statusForeground = Brushes.White;
        _currentWallpaperPath = string.Empty;
        _currentWallpaperName = string.Empty;
        _selectedResolution = ResolutionOptions[0];
        // Services are resolved lazily from the App's background host rather than injected,
        // because the ViewModel is constructed on the UI thread before the host is fully started.
        _switchWallpaper = (Application.Current as App)?.Services?.GetService<ISwitchWallpaper>();
        _checkForUpdates = (Application.Current as App)?.Services?.GetService<ICheckForUpdates>();
        _downloadWallpapers = (Application.Current as App)?.Services?.GetService<IDownloadWallpapers>();
        _selectedFillStyle = FillStyleOptions[0];
        _selectedAnnotationPosition = AnnotationPositionOptions[0];
        _selectedSlideshowOrder = SlideshowOrderOptions.First(o => o.Order == SlideshowOrder.NewestFirst);
        _sources.CollectionChanged += OnSourcesCollectionChanged;

        if (_switchWallpaper is not null)
            _switchWallpaper.WallpaperChanged += OnWallpaperChanged;
    }

    // Called from a background thread when the wallpaper changes; marshals all UI
    // updates to the UI thread so Avalonia bindings stay consistent.
    private void OnWallpaperChanged(string path)
    {
        Dispatcher.UIThread.Post(() =>
        {
            CurrentWallpaperPath = path;
            CurrentWallpaperName = GetDisplayName(path);
            RefreshPreviewImage();
            RefreshFavoriteState();
        });
    }

    // Unhook property-change listeners from the old collection before replacing it,
    // so the old sources don't continue triggering saves after they are discarded.
    partial void OnSourcesChanging(ObservableCollection<WallpaperSource> value)
    {
        _sources.CollectionChanged -= OnSourcesCollectionChanged;
        foreach (var src in _sources)
            src.PropertyChanged -= OnSourcePropertyChanged;
    }

    // Hook property-change listeners onto the new collection and all its initial items.
    partial void OnSourcesChanged(ObservableCollection<WallpaperSource> value)
    {
        value.CollectionChanged += OnSourcesCollectionChanged;
        foreach (var src in value)
            src.PropertyChanged += OnSourcePropertyChanged;
    }

    // Maintains per-item property listeners when items are added or removed from the sources list,
    // so toggling IsEnabled on a source triggers an auto-save.
    private void OnSourcesCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (WallpaperSource src in e.OldItems)
                src.PropertyChanged -= OnSourcePropertyChanged;

        if (e.NewItems is not null)
            foreach (WallpaperSource src in e.NewItems)
                src.PropertyChanged += OnSourcePropertyChanged;

        TriggerSave();
    }

    private void OnSourcePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        TriggerSave();
    }

    // Derives the status bar text colour from the message prefix:
    // ✓ = green (success), ✗ = red (error), anything else = white (neutral/info).
    partial void OnStatusMessageChanged(string value)
    {
        StatusForeground = value.StartsWith("✓") ? new SolidColorBrush(Color.Parse("#4ADE80"))
            : value.StartsWith("✗") ? new SolidColorBrush(Color.Parse("#F87171"))
            : Brushes.White;
    }

    partial void OnFolderChanged(string value) => TriggerSave();
    partial void OnSlideshowCronExpressionChanged(string value) => TriggerSave();
    partial void OnSlideshowIntervalMinutesChanged(int value) => TriggerSave();
    partial void OnSlideshowIntervalHoursChanged(int value) => TriggerSave();
    partial void OnSelectedResolutionChanged(ResolutionOption value) => TriggerSave();
    partial void OnSelectedFillStyleChanged(FillStyleOption value) => TriggerSave();
    partial void OnSelectedSlideshowOrderChanged(SlideshowOrderOption value) => TriggerSave();
    partial void OnRetentionDaysChanged(int? value) => TriggerSave();
    partial void OnAnnotateWallpaperChanged(bool value) => TriggerSave();
    partial void OnAnnotationFontFamilyChanged(string value) => TriggerSave();
    partial void OnAnnotationFontSizeChanged(int value) => TriggerSave();
    partial void OnAnnotationColorChanged(string value) => TriggerSave();
    partial void OnAnnotationOutlineEnabledChanged(bool value) => TriggerSave();
    partial void OnSelectedAnnotationPositionChanged(AnnotationPositionOption value) => TriggerSave();
    partial void OnAutoUpdatesEnabledChanged(bool value) => TriggerSave();
    partial void OnSlideshowEnabledChanged(bool value) => TriggerSave();
    partial void OnDebugModeChanged(bool value) => TriggerSave();
    partial void OnMinimizeToTrayChanged(bool value) => TriggerSave();
    partial void OnFavoritePriorityEnabledChanged(bool value) => TriggerSave();
    partial void OnFavoritePriorityWeightChanged(int value) => TriggerSave();

    partial void OnRunOnStartupChanged(bool value)
    {
        try
        {
#pragma warning disable CA1416
            App.UpdateStartupRegistration(value);
#pragma warning restore CA1416
        }
        catch (Exception ex)
        {
            _ = ShowTransientStatusAsync($"✗ Failed to update startup registration: {ex.Message}");
        }
        TriggerSave();
    }

    // Guards against property-change callbacks firing during LoadAsync (which sets many
    // properties at once) and causing a flood of redundant save operations.
    private void TriggerSave()
    {
        if (_isLoading)
            return;
        _ = SaveSettingsAsync();
    }

    // Populates all ViewModel properties from persisted settings without triggering
    // auto-save. The _isLoading flag suppresses TriggerSave during the batch assignment.
    // Gallery loading is deferred until after the flag is cleared so it can save independently.
    public async Task LoadAsync()
    {
        _isLoading = true;
        try
        {
            var settings = await WallpaperNexusSettings.LoadAsync();
            Folder = settings.Download.Folder;
            // Set the backing field directly to avoid triggering TriggerSave via the setter,
            // then raise property-changed for all derived mode flags manually.
            _slideshowScheduleMode = settings.Slideshow.ScheduleMode;
            OnPropertyChanged(nameof(SlideshowScheduleMode));
            OnPropertyChanged(nameof(IsIntervalMinutesMode));
            OnPropertyChanged(nameof(IsIntervalHoursMode));
            OnPropertyChanged(nameof(IsCronMode));
            SlideshowIntervalMinutes = settings.Slideshow.IntervalMinutes > 0 ? settings.Slideshow.IntervalMinutes : 30;
            SlideshowIntervalHours = settings.Slideshow.IntervalHours > 0 ? settings.Slideshow.IntervalHours : 1;
            SlideshowCronExpression = settings.Slideshow.CronExpression;
            // Match the stored resolution/fill/order values to the corresponding option objects;
            // fall back to index 0 if no matching option is found (e.g. unknown enum value after upgrade).
            SelectedResolution = ResolutionOptions.FirstOrDefault(
                r => r.Width == settings.Download.ResolutionWidth && r.Height == settings.Download.ResolutionHeight)
                ?? ResolutionOptions[0];
            SelectedFillStyle = FillStyleOptions.FirstOrDefault(f => f.Style == settings.Slideshow.FillStyle)
                ?? FillStyleOptions[0];
            SelectedSlideshowOrder = SlideshowOrderOptions.FirstOrDefault(o => o.Order == settings.Slideshow.Order)
                ?? SlideshowOrderOptions[0];
            RetentionDays = settings.Download.RetentionDays;
            AnnotateWallpaper = settings.AnnotateWallpaper;
            AnnotationFontFamily = settings.Annotation.FontFamily;
            AnnotationFontSize = settings.Annotation.FontSize;
            AnnotationColor = settings.Annotation.Color;
            AnnotationOutlineEnabled = settings.Annotation.OutlineEnabled;
            SelectedAnnotationPosition = AnnotationPositionOptions.FirstOrDefault(
                p => p.Position == settings.Annotation.Position) ?? AnnotationPositionOptions[0];
            RunOnStartup = settings.RunOnStartup;
            AutoUpdatesEnabled = settings.AutoUpdatesEnabled;
            SlideshowEnabled = settings.Slideshow.Enabled;
            DebugMode = settings.DebugMode;
            MinimizeToTray = settings.MinimizeToTray;
            FavoritePriorityEnabled = settings.Slideshow.FavoritePriorityEnabled;
            FavoritePriorityWeight = settings.Slideshow.FavoritePriorityWeight > 0 ? settings.Slideshow.FavoritePriorityWeight : 3;

            Sources = new ObservableCollection<WallpaperSource>(settings.Sources);

            var path = settings.CurrentWallpaperPath;
            CurrentWallpaperPath = path;
            CurrentWallpaperName = string.IsNullOrEmpty(path) ? "(none)" : GetDisplayName(path);
            RefreshPreviewImage();
            IsCurrentWallpaperFavorited = !string.IsNullOrEmpty(path)
                && settings.FavoriteWallpapers.Contains(path, StringComparer.OrdinalIgnoreCase);
            FavoriteWallpapers = new ObservableCollection<string>(settings.FavoriteWallpapers);
        }
        finally
        {
            _isLoading = false;
        }

        _ = LoadGallery();
    }

    [RelayCommand]
    private async Task DownloadNow()
    {
        if (_downloadWallpapers is null)
        {
            await ShowTransientStatusAsync("✗ Download service not available.");
            return;
        }

        try
        {
            StatusMessage = "Downloading wallpapers...";
            await Task.Run(_downloadWallpapers.DownloadAllAsync);
            await ShowTransientStatusAsync("✓ Download complete.");
        }
        catch (Exception ex)
        {
            await ShowTransientStatusAsync($"✗ Download failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private void DeleteSource()
    {
        if (SelectedSource is not null)
            Sources.Remove(SelectedSource);
    }

    [RelayCommand]
    private async Task NextWallpaper()
    {
        try
        {
            if (_switchWallpaper is null)
            {
                StatusMessage = "✗ Wallpaper switcher not available.";
                return;
            }

            StatusMessage = "Switching wallpaper...";
            var next = await Task.Run(_switchWallpaper.SwitchToNextAsync);
            // If the folder is empty, trigger a download and retry before giving up
            if (next is null && _downloadWallpapers is not null)
            {
                StatusMessage = "No wallpapers found. Downloading...";
                await Task.Run(_downloadWallpapers.DownloadAllAsync);
                next = await Task.Run(_switchWallpaper.SwitchToNextAsync);
            }
            if (next is null)
            {
                await ShowTransientStatusAsync("✗ No wallpapers found. Check your wallpapers folder setting.");
                return;
            }
            CurrentWallpaperPath = next;
            CurrentWallpaperName = GetDisplayName(next);
            RefreshPreviewImage();
            RefreshFavoriteState();
            await ShowTransientStatusAsync($"✓ Switched to: {CurrentWallpaperName}");
        }
        catch (Exception ex)
        {
            await ShowTransientStatusAsync($"✗ Error switching wallpaper: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task RandomWallpaper()
    {
        try
        {
            if (_switchWallpaper is null)
            {
                StatusMessage = "✗ Wallpaper switcher not available.";
                return;
            }

            StatusMessage = "Picking random wallpaper...";
            var next = await Task.Run(_switchWallpaper.SwitchToRandomAsync);
            if (next is null && _downloadWallpapers is not null)
            {
                StatusMessage = "No wallpapers found. Downloading...";
                await Task.Run(_downloadWallpapers.DownloadAllAsync);
                next = await Task.Run(_switchWallpaper.SwitchToRandomAsync);
            }
            if (next is null)
            {
                await ShowTransientStatusAsync("✗ No wallpapers found. Check your wallpapers folder setting.");
                return;
            }
            CurrentWallpaperPath = next;
            CurrentWallpaperName = GetDisplayName(next);
            RefreshPreviewImage();
            RefreshFavoriteState();
            await ShowTransientStatusAsync($"✓ Switched to: {CurrentWallpaperName}");
        }
        catch (Exception ex)
        {
            await ShowTransientStatusAsync($"✗ Error switching wallpaper: {ex.Message}");
        }
    }

    // Deletes the current wallpaper file from disk, then automatically advances to the
    // next available wallpaper so the desktop is never left showing a missing file.
    [RelayCommand]
    private async Task DeleteCurrentWallpaper()
    {
        var path = CurrentWallpaperPath;
        if (string.IsNullOrEmpty(path))
            return;

        try
        {
            if (File.Exists(path))
                File.Delete(path);

            CurrentWallpaperPath = string.Empty;
            CurrentWallpaperName = "(none)";

            if (_switchWallpaper is null)
            {
                await ShowTransientStatusAsync("✓ Wallpaper deleted.");
                return;
            }

            var next = await Task.Run(_switchWallpaper.SwitchToNextAsync);
            if (next is null)
            {
                await ShowTransientStatusAsync("✓ Wallpaper deleted. No more wallpapers in folder.");
                return;
            }

            CurrentWallpaperPath = next;
            CurrentWallpaperName = GetDisplayName(next);
            RefreshPreviewImage();
            RefreshFavoriteState();
            await ShowTransientStatusAsync($"✓ Deleted and switched to: {CurrentWallpaperName}");
        }
        catch (Exception ex)
        {
            await ShowTransientStatusAsync($"✗ Error deleting wallpaper: {ex.Message}");
        }
    }

    // Toggles the current wallpaper in/out of the favorites list and keeps
    // the FavoriteWallpapers observable collection in sync with the persisted settings.
    [RelayCommand]
    private async Task ToggleFavorite()
    {
        var path = CurrentWallpaperPath;
        if (string.IsNullOrEmpty(path))
            return;

        try
        {
            var settings = await WallpaperNexusSettings.LoadAsync();
            if (settings.FavoriteWallpapers.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                settings.FavoriteWallpapers.RemoveAll(f => f.Equals(path, StringComparison.OrdinalIgnoreCase));
                IsCurrentWallpaperFavorited = false;
                await settings.SaveAsync();
                var toRemove = FavoriteWallpapers.FirstOrDefault(f => f.Equals(path, StringComparison.OrdinalIgnoreCase));
                if (toRemove is not null) FavoriteWallpapers.Remove(toRemove);
                await ShowTransientStatusAsync("✓ Removed from favorites.");
            }
            else
            {
                settings.FavoriteWallpapers.Add(path);
                IsCurrentWallpaperFavorited = true;
                await settings.SaveAsync();
                if (!FavoriteWallpapers.Contains(path, StringComparer.OrdinalIgnoreCase))
                    FavoriteWallpapers.Add(path);
                await ShowTransientStatusAsync("✓ Added to favorites.");
            }
        }
        catch (Exception ex)
        {
            await ShowTransientStatusAsync($"✗ Error updating favorites: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task RemoveFavorite()
    {
        var path = SelectedFavorite;
        if (string.IsNullOrEmpty(path))
            return;
        try
        {
            var settings = await WallpaperNexusSettings.LoadAsync();
            settings.FavoriteWallpapers.RemoveAll(f => f.Equals(path, StringComparison.OrdinalIgnoreCase));
            await settings.SaveAsync();
            var toRemove = FavoriteWallpapers.FirstOrDefault(f => f.Equals(path, StringComparison.OrdinalIgnoreCase));
            if (toRemove is not null) FavoriteWallpapers.Remove(toRemove);
            if (CurrentWallpaperPath.Equals(path, StringComparison.OrdinalIgnoreCase))
                IsCurrentWallpaperFavorited = false;
            await ShowTransientStatusAsync("✓ Removed from favorites.");
        }
        catch (Exception ex)
        {
            await ShowTransientStatusAsync($"✗ Error removing favorite: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task SetFavoriteAsCurrent()
    {
        var path = SelectedFavorite;
        if (string.IsNullOrEmpty(path))
            return;
        try
        {
            if (_switchWallpaper is null)
            {
                await ShowTransientStatusAsync("✗ Wallpaper switcher not available.");
                return;
            }

            StatusMessage = "Switching wallpaper...";
            var result = await Task.Run(() => _switchWallpaper.SwitchToSpecificAsync(path));
            if (result is null)
            {
                await ShowTransientStatusAsync("✗ Wallpaper file not found.");
                return;
            }
            CurrentWallpaperPath = result;
            CurrentWallpaperName = GetDisplayName(result);
            RefreshPreviewImage();
            RefreshFavoriteState();
            await ShowTransientStatusAsync($"✓ Switched to: {CurrentWallpaperName}");
        }
        catch (Exception ex)
        {
            await ShowTransientStatusAsync($"✗ Error switching wallpaper: {ex.Message}");
        }
    }

    // Reloads the preview thumbnail from the processed current.png/current.jpg
    // and disposes the previous bitmap to avoid leaking unmanaged memory.
    internal void RefreshPreviewImage()
    {
        try
        {
            var oldImage = PreviewImage;

            // Load from the processed current.png/current.jpg in the app directory
            var pngPath = Path.Combine(AppContext.BaseDirectory, "current.png");
            var jpgPath = Path.Combine(AppContext.BaseDirectory, "current.jpg");
            // PNG is preferred; fall back to JPEG if the PNG version does not exist
            var previewPath = File.Exists(pngPath) ? pngPath : File.Exists(jpgPath) ? jpgPath : null;

            if (previewPath is not null)
            {
                using var stream = File.OpenRead(previewPath);
                PreviewImage = new Avalonia.Media.Imaging.Bitmap(stream);
            }
            else
            {
                PreviewImage = null;
            }

            // Dispose the old bitmap after the new one is assigned to avoid a blank-frame flash
            oldImage?.Dispose();
        }
        catch
        {
            PreviewImage = null;
        }
    }

    private async void RefreshFavoriteState()
    {
        try
        {
            var path = CurrentWallpaperPath;
            if (string.IsNullOrEmpty(path))
            {
                IsCurrentWallpaperFavorited = false;
                return;
            }
            var settings = await WallpaperNexusSettings.LoadAsync();
            IsCurrentWallpaperFavorited = settings.FavoriteWallpapers.Contains(path, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            IsCurrentWallpaperFavorited = false;
        }
    }

    [RelayCommand]
    private async Task CheckForUpdates()
    {
        if (_checkForUpdates is null)
        {
            await ShowTransientStatusAsync("✗ Update service not available.");
            return;
        }

        var progress = new Progress<string>(msg => StatusMessage = msg);
        try
        {
            await Task.Run(() => _checkForUpdates.CheckAsync(forceUpdate: false, progress: progress));
            await ShowTransientStatusAsync($"✓ Already up to date ({App.AppVersion}).");
        }
        catch (Exception ex)
        {
            await ShowTransientStatusAsync($"✗ Update check failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task CheckForUpdatesForce()
    {
        if (_checkForUpdates is null)
        {
            await ShowTransientStatusAsync("✗ Update service not available.");
            return;
        }

        var progress = new Progress<string>(msg => StatusMessage = msg);
        try
        {
            await Task.Run(() => _checkForUpdates.CheckAsync(forceUpdate: true, progress: progress));
            await ShowTransientStatusAsync($"✓ Already on latest version ({App.AppVersion}).");
        }
        catch (Exception ex)
        {
            await ShowTransientStatusAsync($"✗ Update check failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        var folder = Folder;
        if (string.IsNullOrEmpty(folder))
            return;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _ = ShowTransientStatusAsync($"✗ Could not open folder: {ex.Message}");
        }
    }

    [RelayCommand]
    private void OpenCurrentWallpaper()
    {
        var path = CurrentWallpaperPath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return;
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _ = ShowTransientStatusAsync($"✗ Could not open wallpaper: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ReportBug()
    {
        var version = App.AppVersion;
        var body = $"**App Version:** {version}\n\n**Describe the bug:**\n\n\n**Steps to reproduce:**\n\n\n**Expected behavior:**\n\n";
        var url = "https://github.com/0Keith/PaperNexus/issues/new"
                + "?assignees=claude&labels=bug&title=Bug+Report"
                + "&body=" + Uri.EscapeDataString(body);
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    [RelayCommand]
    private void OpenHomepage()
    {
        Process.Start(new ProcessStartInfo("https://github.com/0Keith/PaperNexus") { UseShellExecute = true });
    }

    // Maps all ViewModel properties back onto a WallpaperNexusSettings instance and persists it.
    // For interval-based schedule modes the cron expression is synthesised from the numeric value;
    // for manual cron mode the raw expression is validated before saving.
    private async Task SaveSettingsAsync()
    {
        try
        {
            var settings = await WallpaperNexusSettings.LoadAsync();
            settings.Download.Folder = Folder;
            settings.Slideshow.ScheduleMode = SlideshowScheduleMode;
            settings.Slideshow.IntervalMinutes = SlideshowIntervalMinutes;
            settings.Slideshow.IntervalHours = SlideshowIntervalHours;
            // Derive the actual cron expression from whichever scheduling mode is active
            var cronExpression = SlideshowScheduleMode switch
            {
                SlideshowScheduleMode.IntervalMinutes => $"*/{SlideshowIntervalMinutes} * * * *",
                SlideshowScheduleMode.IntervalHours => $"0 */{SlideshowIntervalHours} * * *",
                _ => SlideshowCronExpression,
            };
            // Only validate the expression when the user typed it directly — synthesised expressions are always valid
            if (SlideshowScheduleMode == SlideshowScheduleMode.CronExpression)
            {
                try
                {
                    CronExpression.Parse(cronExpression);
                }
                catch (CronFormatException)
                {
                    await ShowTransientStatusAsync("✗ Invalid cron expression.");
                    return;
                }
            }
            settings.Slideshow.CronExpression = cronExpression;
            settings.Download.ResolutionWidth = SelectedResolution.Width;
            settings.Download.ResolutionHeight = SelectedResolution.Height;
            settings.Download.RetentionDays = RetentionDays ?? 365;
            settings.Slideshow.FillStyle = SelectedFillStyle.Style;
            settings.Slideshow.Order = SelectedSlideshowOrder.Order;
            settings.Slideshow.Enabled = SlideshowEnabled;
            settings.AnnotateWallpaper = AnnotateWallpaper;
            settings.Annotation.FontFamily = AnnotationFontFamily;
            settings.Annotation.FontSize = AnnotationFontSize;
            settings.Annotation.Color = AnnotationColor;
            settings.Annotation.OutlineEnabled = AnnotationOutlineEnabled;
            settings.Annotation.Position = SelectedAnnotationPosition.Position;
            settings.RunOnStartup = RunOnStartup;
            settings.AutoUpdatesEnabled = AutoUpdatesEnabled;
            settings.DebugMode = DebugMode;
            settings.MinimizeToTray = MinimizeToTray;
            settings.Slideshow.FavoritePriorityEnabled = FavoritePriorityEnabled;
            settings.Slideshow.FavoritePriorityWeight = FavoritePriorityWeight;
            settings.Sources = Sources.ToList();
            await settings.SaveAsync();
            await ShowTransientStatusAsync("✓ Settings saved.");
        }
        catch (Exception ex)
        {
            StatusMessage = $"✗ Error saving settings: {ex.Message}";
        }
    }

    private static string GetDisplayName(string path)
    {
        var nameWithoutExt = Path.GetFileNameWithoutExtension(path);
        var lastSep = nameWithoutExt.LastIndexOf(" - ", StringComparison.Ordinal);
        return lastSep > 0 ? nameWithoutExt[..lastSep] : nameWithoutExt;
    }

    // Releases all managed resources held by the ViewModel: event subscriptions, cancellation
    // tokens, and unmanaged Avalonia Bitmap objects. Called when the settings window closes.
    internal void Cleanup()
    {
        if (_switchWallpaper is not null)
            _switchWallpaper.WallpaperChanged -= OnWallpaperChanged;

        // Cancel any pending status-clear or gallery-load operations
        _statusCts.Cancel();
        _statusCts.Dispose();

        _galleryCts.Cancel();
        _galleryCts.Dispose();

        // Free the preview bitmap's unmanaged memory
        var image = PreviewImage;
        PreviewImage = null;
        image?.Dispose();

        foreach (var item in GalleryItems)
            item.DisposeThumbnail();
        GalleryItems.Clear();

        foreach (var src in Sources)
            src.PropertyChanged -= OnSourcePropertyChanged;
        Sources.CollectionChanged -= OnSourcesCollectionChanged;
    }

    // Displays a status message for durationMs milliseconds, then clears it.
    // Cancels any previously running transient message so overlapping calls don't race.
    internal async Task ShowTransientStatusAsync(string message, int durationMs = 3000)
    {
        // Cancel the previous delay so the old message doesn't clear the new one prematurely
        _statusCts.Cancel();
        _statusCts = new CancellationTokenSource();
        var cts = _statusCts;

        StatusMessage = message;
        try
        {
            await Task.Delay(durationMs, cts.Token);
            StatusMessage = string.Empty;
        }
        catch (OperationCanceledException) { }
    }

    // Rebuilds the gallery from the wallpapers folder. Cancels any in-progress gallery
    // load (e.g. from a previous folder change) before starting a new one. GalleryItem
    // objects are created synchronously; thumbnails are loaded asynchronously in the background.
    [RelayCommand]
    private async Task LoadGallery()
    {
        try
        {
            // Cancel an ongoing thumbnail load before clearing GalleryItems to avoid
            // a race where LoadThumbnailsAsync tries to set Thumbnail on a cleared item.
            _galleryCts.Cancel();
            _galleryCts = new CancellationTokenSource();
            var ct = _galleryCts.Token;

            var settings = await WallpaperNexusSettings.LoadAsync();
            var folder = settings.Download.Folder;
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                await ShowTransientStatusAsync("✗ Wallpapers folder not configured or not found.");
                return;
            }

            var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg" };
            // Sort newest-first so recently downloaded wallpapers appear at the top
            var files = Directory.EnumerateFiles(folder)
                .Where(f => extensions.Contains(Path.GetExtension(f)))
                .OrderByDescending(f => File.GetLastWriteTime(f))
                .ToList();

            var favorites = new HashSet<string>(settings.FavoriteWallpapers, StringComparer.OrdinalIgnoreCase);
            var banned = new HashSet<string>(settings.BannedWallpapers, StringComparer.OrdinalIgnoreCase);

            // Dispose old thumbnails before clearing to release bitmap memory
            foreach (var item in GalleryItems)
                item.DisposeThumbnail();
            GalleryItems.Clear();

            foreach (var file in files)
            {
                GalleryItems.Add(new GalleryItem(
                    file,
                    favorites.Contains(file),
                    banned.Contains(file),
                    GallerySetAsCurrent,
                    GalleryToggleFavorite,
                    GalleryToggleBan,
                    GalleryDelete));
            }

            // Fire thumbnail loading on a background thread; ct allows it to be cancelled on re-load
            _ = LoadThumbnailsAsync(GalleryItems.ToList(), ct);
            await ShowTransientStatusAsync($"✓ {files.Count} image{(files.Count == 1 ? "" : "s")} loaded.");
        }
        catch (Exception ex)
        {
            await ShowTransientStatusAsync($"✗ Error loading gallery: {ex.Message}");
        }
    }

    // Sequentially loads thumbnails for each gallery item, checking cancellation between items
    // so the loop exits promptly when the gallery is reloaded. The semaphore inside
    // GalleryItem.LoadThumbnailAsync caps concurrent image decodes to 4 at a time.
    private static async Task LoadThumbnailsAsync(List<GalleryItem> items, CancellationToken ct)
    {
        foreach (var item in items)
        {
            if (ct.IsCancellationRequested)
                return;
            var bmp = await GalleryItem.LoadThumbnailAsync(item.FilePath);
            if (ct.IsCancellationRequested)
            {
                // Cancelled after the decode finished — dispose the bitmap to avoid leaking
                bmp?.Dispose();
                return;
            }
            await Dispatcher.UIThread.InvokeAsync(() => item.Thumbnail = bmp);
        }
    }

    private async Task GallerySetAsCurrent(GalleryItem item)
    {
        if (_switchWallpaper is null)
        {
            await ShowTransientStatusAsync("✗ Wallpaper switcher not available.");
            return;
        }
        StatusMessage = "Switching wallpaper...";
        var result = await Task.Run(() => _switchWallpaper.SwitchToSpecificAsync(item.FilePath));
        if (result is null)
        {
            await ShowTransientStatusAsync("✗ Wallpaper file not found.");
            return;
        }
        CurrentWallpaperPath = result;
        CurrentWallpaperName = GetDisplayName(result);
        RefreshPreviewImage();
        RefreshFavoriteState();
        await ShowTransientStatusAsync($"✓ Switched to: {CurrentWallpaperName}");
    }

    // Toggles a gallery item's favorite state and mirrors the change in both the persisted
    // settings and the FavoriteWallpapers observable collection. Also updates
    // IsCurrentWallpaperFavorited if the toggled item happens to be the active wallpaper.
    private async Task GalleryToggleFavorite(GalleryItem item)
    {
        try
        {
            var settings = await WallpaperNexusSettings.LoadAsync();
            if (item.IsFavorite)
            {
                settings.FavoriteWallpapers.RemoveAll(f => f.Equals(item.FilePath, StringComparison.OrdinalIgnoreCase));
                item.IsFavorite = false;
                var toRemove = FavoriteWallpapers.FirstOrDefault(f => f.Equals(item.FilePath, StringComparison.OrdinalIgnoreCase));
                if (toRemove is not null) FavoriteWallpapers.Remove(toRemove);
                if (CurrentWallpaperPath.Equals(item.FilePath, StringComparison.OrdinalIgnoreCase))
                    IsCurrentWallpaperFavorited = false;
            }
            else
            {
                settings.FavoriteWallpapers.Add(item.FilePath);
                item.IsFavorite = true;
                if (!FavoriteWallpapers.Contains(item.FilePath, StringComparer.OrdinalIgnoreCase))
                    FavoriteWallpapers.Add(item.FilePath);
                if (CurrentWallpaperPath.Equals(item.FilePath, StringComparison.OrdinalIgnoreCase))
                    IsCurrentWallpaperFavorited = true;
            }
            await settings.SaveAsync();
        }
        catch (Exception ex)
        {
            await ShowTransientStatusAsync($"✗ Error updating favorites: {ex.Message}");
        }
    }

    private async Task GalleryToggleBan(GalleryItem item)
    {
        try
        {
            var settings = await WallpaperNexusSettings.LoadAsync();
            if (item.IsBanned)
            {
                settings.BannedWallpapers.RemoveAll(f => f.Equals(item.FilePath, StringComparison.OrdinalIgnoreCase));
                item.IsBanned = false;
            }
            else
            {
                settings.BannedWallpapers.Add(item.FilePath);
                item.IsBanned = true;
            }
            await settings.SaveAsync();
        }
        catch (Exception ex)
        {
            await ShowTransientStatusAsync($"✗ Error updating ban list: {ex.Message}");
        }
    }

    // Deletes a wallpaper from disk, removes it from the favorites/ban lists and the
    // gallery collection, and clears the current-wallpaper display if it was the active image.
    private async Task GalleryDelete(GalleryItem item)
    {
        try
        {
            if (File.Exists(item.FilePath))
                File.Delete(item.FilePath);

            // Remove the path from both special lists so stale references don't persist in settings
            var settings = await WallpaperNexusSettings.LoadAsync();
            settings.FavoriteWallpapers.RemoveAll(f => f.Equals(item.FilePath, StringComparison.OrdinalIgnoreCase));
            settings.BannedWallpapers.RemoveAll(f => f.Equals(item.FilePath, StringComparison.OrdinalIgnoreCase));
            await settings.SaveAsync();

            var favToRemove = FavoriteWallpapers.FirstOrDefault(f => f.Equals(item.FilePath, StringComparison.OrdinalIgnoreCase));
            if (favToRemove is not null) FavoriteWallpapers.Remove(favToRemove);
            if (CurrentWallpaperPath.Equals(item.FilePath, StringComparison.OrdinalIgnoreCase))
            {
                CurrentWallpaperPath = string.Empty;
                CurrentWallpaperName = "(none)";
            }

            item.DisposeThumbnail();
            GalleryItems.Remove(item);
            await ShowTransientStatusAsync("✓ Wallpaper deleted.");
        }
        catch (Exception ex)
        {
            await ShowTransientStatusAsync($"✗ Error deleting wallpaper: {ex.Message}");
        }
    }
}
