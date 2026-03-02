using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PaperNexus.Core;

namespace PaperNexus.ViewModels;

public record ResolutionOption(string Label, int Width, int Height)
{
    public override string ToString() => Label;
}

public record FillStyleOption(string Label, WallpaperFillStyle Style)
{
    public override string ToString() => Label;
}

public record SwitchPatternOption(string Label, WallpaperSwitchPattern Pattern)
{
    public override string ToString() => Label;
}

public partial class WallpaperConfigViewModel : ObservableObject
{
    public static readonly IReadOnlyList<ResolutionOption> ResolutionOptions = new[]
    {
        new ResolutionOption("Native",                 0,    0),
        new ResolutionOption("HD (1280×720)",       1280,  720),
        new ResolutionOption("Full HD (1920×1080)", 1920, 1080),
        new ResolutionOption("2K (2560×1440)",      2560, 1440),
        new ResolutionOption("4K (3840×2160)",      3840, 2160),
        new ResolutionOption("5K (5120×2880)",      5120, 2880),
        new ResolutionOption("8K (7680×4320)",      7680, 4320),
    };

    private readonly ISwitchWallpaper? _switchWallpaper;
    private readonly ICheckForUpdates? _checkForUpdates;

    public static readonly IReadOnlyList<FillStyleOption> FillStyleOptions = new[]
    {
        new FillStyleOption("Fill",    WallpaperFillStyle.Fill),
        new FillStyleOption("Fit",     WallpaperFillStyle.Fit),
        new FillStyleOption("Stretch", WallpaperFillStyle.Stretch),
        new FillStyleOption("Tile",    WallpaperFillStyle.Tile),
        new FillStyleOption("Center",  WallpaperFillStyle.Center),
        new FillStyleOption("Span",    WallpaperFillStyle.Span),
    };

    public static readonly IReadOnlyList<SwitchPatternOption> SwitchPatternOptions = new[]
    {
        new SwitchPatternOption("Alphabetical", WallpaperSwitchPattern.Alphabetical),
        new SwitchPatternOption("Oldest first", WallpaperSwitchPattern.OldestFirst),
        new SwitchPatternOption("Newest first", WallpaperSwitchPattern.NewestFirst),
        new SwitchPatternOption("Random",       WallpaperSwitchPattern.Random),
    };

    [ObservableProperty]
    private string _folder;

    [ObservableProperty]
    private string _slideshowCronExpression;

    [ObservableProperty]
    private int _slideshowIntervalMinutes;

    [ObservableProperty]
    private int _slideshowIntervalHours;

    private SlideshowScheduleMode _slideshowScheduleMode;

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
    private SwitchPatternOption _selectedSlideshowPattern;

    [ObservableProperty]
    private int _retentionDays;

    [ObservableProperty]
    private bool _annotateWallpaper = true;

    [ObservableProperty]
    private bool _runOnStartup = true;

    [ObservableProperty]
    private bool _slideshowEnabled = true;

    [ObservableProperty]
    private string _statusMessage;

    [ObservableProperty]
    private IBrush _statusForeground;

    [ObservableProperty]
    private string _currentWallpaperPath;

    [ObservableProperty]
    private string _currentWallpaperName;

    [ObservableProperty]
    private ObservableCollection<WallpaperSource> _sources = new();

    [ObservableProperty]
    private WallpaperSource? _selectedSource;

    private bool _isLoading;
    private CancellationTokenSource _statusCts = new();


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
        _switchWallpaper = (Application.Current as App)?.Services?.GetService<ISwitchWallpaper>();
        _checkForUpdates = (Application.Current as App)?.Services?.GetService<ICheckForUpdates>();
        _selectedFillStyle = FillStyleOptions[0];
        _selectedSlideshowPattern = SwitchPatternOptions.First(p => p.Pattern == WallpaperSwitchPattern.NewestFirst);
        _sources.CollectionChanged += OnSourcesCollectionChanged;
    }

    partial void OnSourcesChanging(ObservableCollection<WallpaperSource> value)
    {
        _sources.CollectionChanged -= OnSourcesCollectionChanged;
        foreach (var src in _sources)
            src.PropertyChanged -= OnSourcePropertyChanged;
    }

    partial void OnSourcesChanged(ObservableCollection<WallpaperSource> value)
    {
        value.CollectionChanged += OnSourcesCollectionChanged;
        foreach (var src in value)
            src.PropertyChanged += OnSourcePropertyChanged;
    }

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
    partial void OnSelectedSlideshowPatternChanged(SwitchPatternOption value) => TriggerSave();
    partial void OnRetentionDaysChanged(int value) => TriggerSave();
    partial void OnAnnotateWallpaperChanged(bool value) => TriggerSave();
    partial void OnSlideshowEnabledChanged(bool value) => TriggerSave();

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

    private void TriggerSave()
    {
        if (_isLoading)
            return;
        _ = SaveSettingsAsync();
    }

    public async Task LoadAsync()
    {
        _isLoading = true;
        try
        {
            var settings = await WallpaperNexusSettings.LoadAsync();
            Folder = settings.Download.Folder;
            _slideshowScheduleMode = settings.Slideshow.ScheduleMode;
            OnPropertyChanged(nameof(SlideshowScheduleMode));
            OnPropertyChanged(nameof(IsIntervalMinutesMode));
            OnPropertyChanged(nameof(IsIntervalHoursMode));
            OnPropertyChanged(nameof(IsCronMode));
            SlideshowIntervalMinutes = settings.Slideshow.IntervalMinutes > 0 ? settings.Slideshow.IntervalMinutes : 30;
            SlideshowIntervalHours = settings.Slideshow.IntervalHours > 0 ? settings.Slideshow.IntervalHours : 1;
            SlideshowCronExpression = settings.Slideshow.CronExpression;
            SelectedResolution = ResolutionOptions.FirstOrDefault(
                r => r.Width == settings.Download.ResolutionWidth && r.Height == settings.Download.ResolutionHeight)
                ?? ResolutionOptions[0];
            SelectedFillStyle = FillStyleOptions.FirstOrDefault(f => f.Style == settings.Slideshow.FillStyle)
                ?? FillStyleOptions[0];
            SelectedSlideshowPattern = SwitchPatternOptions.FirstOrDefault(p => p.Pattern == settings.Slideshow.Pattern)
                ?? SwitchPatternOptions[0];
            RetentionDays = settings.Download.RetentionDays;
            AnnotateWallpaper = settings.AnnotateWallpaper;
            RunOnStartup = settings.RunOnStartup;
            SlideshowEnabled = settings.Slideshow.Enabled;

            Sources = new ObservableCollection<WallpaperSource>(settings.Sources);

            var path = settings.CurrentWallpaperPath;
            CurrentWallpaperPath = path;
            CurrentWallpaperName = string.IsNullOrEmpty(path) ? "(none)" : GetDisplayName(path);
        }
        finally
        {
            _isLoading = false;
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
            if (next is null)
            {
                await ShowTransientStatusAsync("✗ No wallpapers found. Check your wallpapers folder setting.");
                return;
            }
            CurrentWallpaperPath = next;
            CurrentWallpaperName = GetDisplayName(next);
            await ShowTransientStatusAsync($"✓ Switched to: {CurrentWallpaperName}");
        }
        catch (Exception ex)
        {
            await ShowTransientStatusAsync($"✗ Error switching wallpaper: {ex.Message}");
        }
    }

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
            await ShowTransientStatusAsync($"✓ Deleted and switched to: {CurrentWallpaperName}");
        }
        catch (Exception ex)
        {
            await ShowTransientStatusAsync($"✗ Error deleting wallpaper: {ex.Message}");
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
            await ShowTransientStatusAsync("✓ Already up to date.");
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
            await ShowTransientStatusAsync("✓ Already up to date.");
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

    private async Task SaveSettingsAsync()
    {
        try
        {
            var settings = await WallpaperNexusSettings.LoadAsync();
            settings.Download.Folder = Folder;
            settings.Slideshow.ScheduleMode = SlideshowScheduleMode;
            settings.Slideshow.IntervalMinutes = SlideshowIntervalMinutes;
            settings.Slideshow.IntervalHours = SlideshowIntervalHours;
            settings.Slideshow.CronExpression = SlideshowScheduleMode switch
            {
                SlideshowScheduleMode.IntervalMinutes => $"*/{SlideshowIntervalMinutes} * * * *",
                SlideshowScheduleMode.IntervalHours => $"0 */{SlideshowIntervalHours} * * *",
                _ => SlideshowCronExpression,
            };
            settings.Download.ResolutionWidth = SelectedResolution.Width;
            settings.Download.ResolutionHeight = SelectedResolution.Height;
            settings.Download.RetentionDays = RetentionDays;
            settings.Slideshow.FillStyle = SelectedFillStyle.Style;
            settings.Slideshow.Pattern = SelectedSlideshowPattern.Pattern;
            settings.Slideshow.Enabled = SlideshowEnabled;
            settings.AnnotateWallpaper = AnnotateWallpaper;
            settings.RunOnStartup = RunOnStartup;
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
        return lastSep > 0 ? nameWithoutExt[..lastSep] : Path.GetFileName(path);
    }

    internal async Task ShowTransientStatusAsync(string message, int durationMs = 3000)
    {
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
}
