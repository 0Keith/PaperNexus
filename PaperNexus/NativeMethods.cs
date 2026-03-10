using System.Runtime.InteropServices;

namespace PaperNexus;

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
