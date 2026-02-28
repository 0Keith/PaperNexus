using System.Runtime.InteropServices;

namespace Excogitated.WallpaperNexus;

internal static class NativeMethods
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int SystemParametersInfo(int uAction, int uParam, string lpvParam, int fuWinIni);

    private const int SPI_SETDESKWALLPAPER = 0x0014;
    private const int SPIF_UPDATEINIFILE = 0x01;
    private const int SPIF_SENDCHANGE = 0x02;

    internal static void SetDesktopWallpaper(string wallpaperPath)
    {
        SystemParametersInfo(
            SPI_SETDESKWALLPAPER,
            0,
            wallpaperPath,
            SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
    }
}
