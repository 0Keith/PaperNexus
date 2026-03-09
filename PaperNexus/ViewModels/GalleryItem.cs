using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace PaperNexus.ViewModels;

public partial class GalleryItem : ObservableObject
{
    [ObservableProperty]
    private Bitmap? _thumbnail;

    [ObservableProperty]
    private bool _isFavorite;

    [ObservableProperty]
    private bool _isBanned;

    public string FilePath { get; }
    public string DisplayName { get; }

    private readonly Func<GalleryItem, Task> _setAsCurrent;
    private readonly Func<GalleryItem, Task> _toggleFavorite;
    private readonly Func<GalleryItem, Task> _toggleBan;
    private readonly Func<GalleryItem, Task> _delete;

    // Stores the action delegates as callbacks so the item can trigger ViewModel operations
    // without holding a direct reference to the ViewModel (avoids tight coupling).
    public GalleryItem(
        string path,
        bool isFavorite,
        bool isBanned,
        Func<GalleryItem, Task> setAsCurrent,
        Func<GalleryItem, Task> toggleFavorite,
        Func<GalleryItem, Task> toggleBan,
        Func<GalleryItem, Task> delete)
    {
        FilePath = path;
        DisplayName = GetDisplayName(path);
        _isFavorite = isFavorite;
        _isBanned = isBanned;
        _setAsCurrent = setAsCurrent;
        _toggleFavorite = toggleFavorite;
        _toggleBan = toggleBan;
        _delete = delete;
    }

    [RelayCommand]
    private Task SetAsCurrent() => _setAsCurrent(this);

    [RelayCommand]
    private Task ToggleFavorite() => _toggleFavorite(this);

    [RelayCommand]
    private Task ToggleBan() => _toggleBan(this);

    [RelayCommand]
    private Task Delete() => _delete(this);

    // Clears the Thumbnail property before disposing so bindings see null immediately
    // and do not attempt to render the bitmap after it is freed.
    public void DisposeThumbnail()
    {
        var bmp = Thumbnail;
        Thumbnail = null;
        bmp?.Dispose();
    }

    // Decodes the image at path, scales it to 200 px wide (height proportional), and
    // returns an Avalonia Bitmap. The semaphore limits concurrent decodes to 4 to
    // avoid exhausting memory when the gallery is large. Returns null on any error.
    internal static async Task<Bitmap?> LoadThumbnailAsync(string path)
    {
        try
        {
            await _thumbnailSemaphore.WaitAsync();
            try
            {
                using var img = await Image.LoadAsync(path);
                // Width=200, Height=0 → height is computed to preserve aspect ratio
                img.Mutate(x => x.Resize(200, 0));
                using var ms = new MemoryStream();
                await img.SaveAsPngAsync(ms);
                ms.Position = 0;
                return new Bitmap(ms);
            }
            finally
            {
                _thumbnailSemaphore.Release();
            }
        }
        catch
        {
            return null;
        }
    }

    private static readonly SemaphoreSlim _thumbnailSemaphore = new(4, 4);

    private static string GetDisplayName(string path)
    {
        var nameWithoutExt = Path.GetFileNameWithoutExtension(path);
        var lastSep = nameWithoutExt.LastIndexOf(" - ", StringComparison.Ordinal);
        return lastSep > 0 ? nameWithoutExt[..lastSep] : nameWithoutExt;
    }
}
