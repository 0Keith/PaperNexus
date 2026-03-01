using System.Collections.ObjectModel;
using Avalonia;
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
        new SwitchPatternOption("Oldest first", WallpaperSwitchPattern.Oldest),
        new SwitchPatternOption("Newest first", WallpaperSwitchPattern.Newest),
        new SwitchPatternOption("Random",       WallpaperSwitchPattern.Random),
        new SwitchPatternOption("Never",        WallpaperSwitchPattern.Never),
    };

    [ObservableProperty]
    private string _wallpapersFolder;

    [ObservableProperty]
    private string _switchCronExpression;

    [ObservableProperty]
    private ResolutionOption _selectedResolution;

    [ObservableProperty]
    private FillStyleOption _selectedFillStyle;

    [ObservableProperty]
    private SwitchPatternOption _selectedSwitchPattern;

    [ObservableProperty]
    private int _retentionDays;

    [ObservableProperty]
    private bool _annotateWallpaper = true;

    [ObservableProperty]
    private string _statusMessage;

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

    [ObservableProperty]
    private string _editName = string.Empty;

    [ObservableProperty]
    private string _editUrl = string.Empty;

    [ObservableProperty]
    private string _editCronExpression = "0 * * * *";

    public WallpaperConfigViewModel()
    {
        _wallpapersFolder = string.Empty;
        _switchCronExpression = string.Empty;
        _statusMessage = string.Empty;
        _currentWallpaperPath = string.Empty;
        _currentWallpaperName = string.Empty;
        _selectedResolution = ResolutionOptions[0];
        _switchWallpaper = (Application.Current as App)?.Services?.GetService<ISwitchWallpaper>();
        _selectedFillStyle = FillStyleOptions[0];
        _selectedSwitchPattern = SwitchPatternOptions.First(p => p.Pattern == WallpaperSwitchPattern.Newest);
        _sources.CollectionChanged += OnSourcesCollectionChanged;
    }

    partial void OnSelectedSourceChanged(WallpaperSource? value)
    {
        EditName = value?.Name ?? string.Empty;
        EditUrl = value?.Url ?? string.Empty;
        EditCronExpression = value?.CronExpression ?? "0 * * * *";
    }

    partial void OnSourcesChanging(ObservableCollection<WallpaperSource> value)
    {
        _sources.CollectionChanged -= OnSourcesCollectionChanged;
    }

    partial void OnSourcesChanged(ObservableCollection<WallpaperSource> value)
    {
        value.CollectionChanged += OnSourcesCollectionChanged;
    }

    private void OnSourcesCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        TriggerSave();
    }

    partial void OnWallpapersFolderChanged(string value) => TriggerSave();
    partial void OnSwitchCronExpressionChanged(string value) => TriggerSave();
    partial void OnSelectedResolutionChanged(ResolutionOption value) => TriggerSave();
    partial void OnSelectedFillStyleChanged(FillStyleOption value) => TriggerSave();
    partial void OnSelectedSwitchPatternChanged(SwitchPatternOption value) => TriggerSave();
    partial void OnRetentionDaysChanged(int value) => TriggerSave();
    partial void OnAnnotateWallpaperChanged(bool value) => TriggerSave();

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
            WallpapersFolder = settings.WallpapersFolder;
            SwitchCronExpression = settings.SwitchCronExpression;
            SelectedResolution = ResolutionOptions.FirstOrDefault(
                r => r.Width == settings.ImageWidth && r.Height == settings.ImageHeight)
                ?? ResolutionOptions[0];
            SelectedFillStyle = FillStyleOptions.FirstOrDefault(f => f.Style == settings.FillStyle)
                ?? FillStyleOptions[0];
            SelectedSwitchPattern = SwitchPatternOptions.FirstOrDefault(p => p.Pattern == settings.SwitchPattern)
                ?? SwitchPatternOptions[0];
            RetentionDays = settings.RetentionDays;
            AnnotateWallpaper = settings.AnnotateWallpaper;

            Sources = new ObservableCollection<WallpaperSource>(settings.Sources);

            var path = settings.CurrentWallpaperPath;
            CurrentWallpaperPath = path;
            CurrentWallpaperName = string.IsNullOrEmpty(path) ? "(none)" : Path.GetFileName(path);
        }
        finally
        {
            _isLoading = false;
        }
    }

    [RelayCommand]
    private void AddSource()
    {
        var source = new WallpaperSource { Name = "New Source", Url = string.Empty };
        Sources.Add(source);
        SelectedSource = source;
    }

    [RelayCommand]
    private void DeleteSource()
    {
        if (SelectedSource is not null)
            Sources.Remove(SelectedSource);
    }

    [RelayCommand]
    private void ApplySourceEdit()
    {
        if (SelectedSource is null)
            return;
        var index = Sources.IndexOf(SelectedSource);
        Sources.RemoveAt(index);
        var updated = new WallpaperSource { Name = EditName, Url = EditUrl, CronExpression = EditCronExpression };
        Sources.Insert(index, updated);
        SelectedSource = updated;
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

            var next = await Task.Run(_switchWallpaper.SwitchToNextAsync);
            if (next is null)
            {
                StatusMessage = "✗ No wallpapers found. Check your wallpapers folder setting.";
                return;
            }
            CurrentWallpaperPath = next;
            CurrentWallpaperName = Path.GetFileName(next);
            StatusMessage = $"✓ Switched to: {CurrentWallpaperName}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"✗ Error switching wallpaper: {ex.Message}";
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
                StatusMessage = "✓ Wallpaper deleted.";
                return;
            }

            var next = await Task.Run(_switchWallpaper.SwitchToNextAsync);
            if (next is null)
            {
                StatusMessage = "✓ Wallpaper deleted. No more wallpapers in folder.";
                return;
            }

            CurrentWallpaperPath = next;
            CurrentWallpaperName = Path.GetFileName(next);
            StatusMessage = $"✓ Deleted and switched to: {CurrentWallpaperName}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"✗ Error deleting wallpaper: {ex.Message}";
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
            settings.WallpapersFolder = WallpapersFolder;
            settings.SwitchCronExpression = SwitchCronExpression;
            settings.ImageWidth = SelectedResolution.Width;
            settings.ImageHeight = SelectedResolution.Height;
            settings.RetentionDays = RetentionDays;
            settings.FillStyle = SelectedFillStyle.Style;
            settings.SwitchPattern = SelectedSwitchPattern.Pattern;
            settings.AnnotateWallpaper = AnnotateWallpaper;
            settings.Sources = Sources.ToList();
            await settings.SaveAsync();
            await ShowTransientStatusAsync("✓ Settings saved.");
        }
        catch (Exception ex)
        {
            StatusMessage = $"✗ Error saving settings: {ex.Message}";
        }
    }

    private async Task ShowTransientStatusAsync(string message, int durationMs = 3000)
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
