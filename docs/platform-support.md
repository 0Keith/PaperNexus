# Platform Support

PaperNexus runs on Windows and Linux from a single `net10.0` assembly. Every place the two
operating systems differ is isolated in `PaperNexus/Core/Platform/`; no other file may call
a Windows-only API, hardcode `.exe`, or compare paths case-insensitively.

## The platform layer

| File | Concern | Windows | Linux |
| --- | --- | --- | --- |
| `PlatformPaths.cs` | Executable name, install/pictures directories, path comparison, execute bit | `PaperNexus.exe`, `%LOCALAPPDATA%`, case-insensitive compare | `PaperNexus`, `~/.local/share`, case-sensitive compare, `chmod +x` |
| `SingleInstance.cs` | One running copy, plus "show the window" from a second launch | Named `Mutex` + named `EventWaitHandle` | Unix domain socket in `$XDG_RUNTIME_DIR` |
| `StartupRegistration.cs` | Launch at login | `HKCU\...\CurrentVersion\Run` | `~/.config/autostart/PaperNexus.desktop` |
| `IWallpaperBackend.cs`, `LinuxWallpaperBackend.cs`, `NativeMethods.cs` | Setting the desktop wallpaper and its fill style | `SystemParametersInfo` + `HKCU\Control Panel\Desktop` | KDE Plasma / GNOME / generic setters |
| `LinuxDesktop.cs` | Desktop environment detection, running helper commands | not used | `XDG_CURRENT_DESKTOP` etc., then process-name inference |
| `ShellOpener.cs` | Opening folders, URLs, and relaunching the app | `explorer.exe`, shell execute | `xdg-open`, shell execute |

`WallpaperApplier` remains the single type registered for `IWallpaperApplier`, so the
`IAddSingleton<T>` auto-discovery in `Bootstrapper` still resolves exactly one
implementation; it selects a backend in its constructor rather than being replaced.

## Setting the wallpaper on Linux

There is no cross-desktop API, so `LinuxWallpaperBackend` dispatches on the detected desktop:

- **KDE Plasma** (the SteamOS Desktop Mode shell) - a JavaScript snippet passed to
  `org.kde.PlasmaShell.evaluateScript` over D-Bus, tried against `qdbus6`, `qdbus-qt6`,
  `qdbus`, then `qdbus-qt5`. Falls back to `plasma-apply-wallpaperimage`, which sets the
  image but not the fill mode.
- **GNOME** - `gsettings` keys under `org.gnome.desktop.background`. Both `picture-uri` and
  `picture-uri-dark` are written, otherwise the wallpaper appears unchanged under the dark
  colour scheme.
- **Anything else** - `feh`, then `xwallpaper`, then `swaybg` if installed.

Both KDE and GNOME only repaint when the stored value actually changes, and PaperNexus always
writes the same `current.png`, so each backend clears the key before writing the real path.
`swaybg` runs for as long as the wallpaper is displayed, so the backend keeps its process and
terminates the old instance only after the replacement is running.

## Auto-update

The updater looks up a release asset named for the running platform: `PaperNexus.exe` on
Windows, `PaperNexus-linux-x64` on Linux. Integrity is verified before the binary is ever
executed:

- Windows - Authenticode signature with subject `CN=PaperNexus`.
- Linux - SHA-256 digest read from the `PaperNexus-linux-x64.sha256` sidecar asset.
  Authenticode is a PE-only format, so it cannot be used. **An update with no checksum asset
  is refused**, so the deploy workflow must keep publishing that file.

The binary swap runs from a self-deleting script - a `.bat` driven by `cmd.exe` on Windows,
a `.sh` driven by `/bin/sh` on Linux - with the same back up, move, relaunch, verify,
roll back sequence on both.

## Building and releasing

The deploy workflow publishes a self-contained single-file binary per runtime identifier
(`win-x64`, `linux-x64`) and attaches three assets to the release: the signed Windows exe,
the Linux binary, and the Linux checksum.

## Verifying a change locally

This repository is developed on Ubuntu with GNOME; SteamOS Desktop Mode (KDE Plasma) is the
Linux deployment target. To drive the real desktop from a non-graphical shell, export the
session's variables before launching:

```bash
export XDG_RUNTIME_DIR=/run/user/$(id -u)
export DBUS_SESSION_BUS_ADDRESS=unix:path=$XDG_RUNTIME_DIR/bus
export DISPLAY=:0 XAUTHORITY=$(pgrep -a Xwayland | grep -o '/run/user/[0-9]*/\.mutter-Xwaylandauth\.[A-Za-z0-9]*')
dotnet run --project PaperNexus -c Release -- --debug
```

Confirm the wallpaper actually changed by reading the desktop's own state rather than the
app's log - `gsettings get org.gnome.desktop.background picture-uri` on GNOME.
