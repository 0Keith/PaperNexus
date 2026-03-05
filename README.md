<p align="center">
  <img src="PaperNexus/Assets/logo.png" alt="PaperNexus Logo" width="128" />
</p>

<h1 align="center">PaperNexus</h1>

<p align="center">
  <em>Because life's too short to right-click > "Set as desktop background" every day.</em>
</p>

<p align="center">
  <a href="https://github.com/0Keith/PaperNexus/releases/latest"><img src="https://img.shields.io/github/v/release/0Keith/PaperNexus?style=flat-square&color=4c9a6e" alt="Latest Release" /></a>
  <a href="https://github.com/0Keith/PaperNexus/blob/main/LICENSE"><img src="https://img.shields.io/github/license/0Keith/PaperNexus?style=flat-square&color=4c9a6e" alt="License" /></a>
  <img src="https://img.shields.io/badge/.NET-10.0-512bd4?style=flat-square" alt=".NET 10" />
  <img src="https://img.shields.io/badge/platform-Windows-0078d4?style=flat-square" alt="Windows" />
</p>

---

PaperNexus is an automated wallpaper rotation app for Windows that lives quietly in your system tray, fetching gorgeous wallpapers and cycling through them on your schedule. Set it, forget it, and enjoy a fresh desktop every time you glance at it.

## What It Does

- **Fetches wallpapers automatically** from online sources (Bing Daily and more)
- **Rotates your desktop** on a schedule — cron expressions, intervals, you name it
- **Runs silently** in the system tray, minding its own business
- **Annotates wallpapers** with optional title overlays (with auto-contrasting outline) so you know what you're looking at
- **Updates itself** in the background — no manual downloads required
- **Starts with Windows** so your desktop is never boring, even on a Monday morning

## Quick Start

1. Grab the latest `PaperNexus.exe` from [Releases](https://github.com/0Keith/PaperNexus/releases/latest)
2. Run it
3. That's it. You're done. Go get a coffee.

PaperNexus will park itself in your system tray and start doing its thing. Right-click the tray icon for options:

| Action | What Happens |
|---|---|
| **Open Settings** | Configure sources, schedules, and display options |
| **Next Wallpaper** | Can't wait? Skip ahead immediately |
| **Exit** | Say goodbye (but why would you?) |

## Building From Source

```bash
# Clone the repo
git clone https://github.com/0Keith/PaperNexus.git
cd PaperNexus

# Restore & build
dotnet restore PaperNexus.sln
dotnet build PaperNexus.sln --configuration Release

# Run it
dotnet run --project PaperNexus

# Or publish a self-contained exe
dotnet publish PaperNexus/PaperNexus.csproj \
  -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:PublishTrimmed=false
```

Requires [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

## How It Works

```
  You ──────── "I want pretty wallpapers"
                        │
                        ▼
              ┌─────────────────┐
              │   PaperNexus    │
              │  (system tray)  │
              └────────┬────────┘
                       │
          ┌────────────┼────────────┐
          ▼            ▼            ▼
    ┌──────────┐ ┌──────────┐ ┌──────────┐
    │ Download │ │  Switch  │ │  Auto    │
    │ Service  │ │ Service  │ │  Update  │
    └────┬─────┘ └────┬─────┘ └────┬─────┘
         │            │            │
         ▼            ▼            ▼
    Fetch from    Rotate &     Check GitHub
    online feeds  set desktop  for new builds
```

Under the hood, PaperNexus uses scheduled background services to keep everything humming. Wallpapers are downloaded, optionally annotated, and set as your desktop background — all without you lifting a finger.

## Tech Stack

| Component | Technology |
|---|---|
| **Framework** | .NET 10.0 |
| **UI** | [Avalonia UI](https://avaloniaui.net/) 11.3 |
| **Architecture** | MVVM with [CommunityToolkit.Mvvm](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/) |
| **Image Processing** | [SixLabors.ImageSharp](https://sixlabors.com/products/imagesharp/) |
| **Scheduling** | [Cronos](https://github.com/HangfireIO/Cronos) cron expressions |
| **DI & Hosting** | Microsoft.Extensions.Hosting |

## FAQ

**Q: Does it work on Mac/Linux?**
A: The UI framework (Avalonia) is cross-platform, but wallpaper-setting uses Windows APIs. So for now, Windows only. PRs welcome though!

**Q: Will it eat my bandwidth?**
A: Nah. It downloads wallpapers on a schedule (default: once daily from Bing) and caches them locally. Sipping, not chugging.

**Q: SmartScreen is yelling at me!**
A: The app is signed with a self-signed certificate. SmartScreen calms down after a few people download the same release. Click "More info" > "Run anyway" if you trust us (and you should, the code is right here).

**Q: Can I add my own wallpaper sources?**
A: Yes! Open Settings and add any HTTP feed URL that serves wallpaper images. Go wild.

**Q: It updated itself. Should I be concerned?**
A: Only if you're concerned about getting bug fixes and features automatically. Updates come straight from this GitHub repo's Releases page.

## Contributing

Found a bug? Have a feature idea? Want to add Linux wallpaper support and become a hero?

1. Fork the repo
2. Create a branch (`git checkout -b my-cool-feature`)
3. Make your changes
4. Open a PR

## License

See [LICENSE](LICENSE) for details.

---

<p align="center">
  <sub>Made for people who believe their desktop deserves better than the default Windows wallpaper.</sub>
</p>
