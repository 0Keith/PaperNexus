using System.Runtime.InteropServices;
using Microsoft.Win32;
using PaperNexus.Core;

namespace PaperNexus;

// Abstraction over Windows desktop API calls so tests can run without changing the real wallpaper
public interface IWallpaperApplier
{
    public bool SetWallpaper(string wallpaperPath);
    public void ApplyFillStyle(WallpaperFillStyle style);
}

internal sealed class WallpaperApplier : IWallpaperApplier, IAddSingleton<IWallpaperApplier>
{
    public bool SetWallpaper(string wallpaperPath) => NativeMethods.SetDesktopWallpaper(wallpaperPath);

    public void ApplyFillStyle(WallpaperFillStyle style)
    {
        if (!OperatingSystem.IsWindows())
            return;

        // WallpaperStyle and TileWallpaper registry values under HKCU\Control Panel\Desktop
        // control how Windows positions the wallpaper image.
        var (wallpaperStyle, tileWallpaper) = style switch
        {
            WallpaperFillStyle.Tile => ("0", "1"),
            WallpaperFillStyle.Center => ("0", "0"),
            WallpaperFillStyle.Stretch => ("2", "0"),
            WallpaperFillStyle.Fit => ("6", "0"),
            WallpaperFillStyle.Fill => ("10", "0"),
            WallpaperFillStyle.Span => ("22", "0"),
            _ => ("10", "0"),
        };

        using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", writable: true);
        key?.SetValue("WallpaperStyle", wallpaperStyle);
        key?.SetValue("TileWallpaper", tileWallpaper);
    }
}

internal static class NativeMethods
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int SystemParametersInfo(int uAction, int uParam, string lpvParam, int fuWinIni);

    private const int SPI_SETDESKWALLPAPER = 0x0014;
    private const int SPIF_UPDATEINIFILE = 0x01;
    private const int SPIF_SENDCHANGE = 0x02;

    // Tells Windows to apply the image at wallpaperPath as the desktop wallpaper.
    // SPIF_UPDATEINIFILE persists the change to the user profile;
    // SPIF_SENDCHANGE broadcasts WM_SETTINGCHANGE so the shell picks it up immediately.
    // Returns true if the API call succeeded (non-zero return), false otherwise.
    internal static bool SetDesktopWallpaper(string wallpaperPath)
    {
        var result = SystemParametersInfo(
            SPI_SETDESKWALLPAPER,
            0,
            wallpaperPath,
            SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
        return result != 0;
    }
}
