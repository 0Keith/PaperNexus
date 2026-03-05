# CLAUDE.md - AI Assistant Guide for PaperNexus

## Project Overview

.NET 10.0 Avalonia desktop app for automated wallpaper rotation on Windows. Solution: `PaperNexus.sln`.

## Quick Reference

```bash
dotnet restore PaperNexus.sln
dotnet build PaperNexus.sln --configuration Release
dotnet run --project PaperNexus
dotnet test --configuration Release
```

## Repository Structure

```
PaperNexus/
├── PaperNexus.sln
├── CLAUDE.md, .editorconfig, .gitignore
├── .claude/                        # Config, hooks, commands
├── .github/workflows/              # pull-request.yml, deploy-wallpaper-service.yml
├── PaperNexus.Tests/               # Unit tests (xUnit + NSubstitute)
└── PaperNexus/                     # Main project
    ├── App.axaml(.cs)              # App root, tray icon, startup
    ├── Program.cs                  # Entry point, auto-install, single-instance, IPC
    ├── AutoUpdateService.cs        # Silent auto-update via GitHub Releases
    ├── DownloadWallpapers.cs       # Scheduled wallpaper downloader
    ├── HttpWallpaperSourceService.cs # HTTP feed client
    ├── NativeMethods.cs            # P/Invoke for Windows wallpaper API
    ├── SwitchWallpaper.cs          # Wallpaper switching logic
    ├── Core/                       # DI, logging, scheduling, settings
    ├── ViewModels/                 # MVVM ViewModels (CommunityToolkit.Mvvm)
    └── Views/                      # Avalonia AXAML views
```

## Key Architecture

- **MVVM** with `CommunityToolkit.Mvvm` (`[ObservableProperty]`, `[RelayCommand]`)
- **Scheduled Jobs (preferred):** `IScheduleScopedJob` — separate business logic from scheduling. Job wrapper delegates to injected interface. Auto-discovered by `AddServicesFrom()`.
- **Scheduled Jobs (legacy):** `ScheduledJobService` base class — `DownloadWallpapers` extends directly. Registered via `IAddHostedSingleton<T>`.
- **DI:** `AddServicesFrom(assembly)` auto-discovers `IAddSingleton<T>`, `IAddHostedSingleton<T>`, and `IScheduleScopedJob` implementations.
- **Auto-Update:** Queries GitHub Releases API, compares `vN` tag as integer against `Assembly.Version.Major`, downloads exe, swaps via self-deleting batch script with rollback.
- **Auto-Install:** First run copies exe to `%LOCALAPPDATA%\PaperNexus\`, migrates settings, relaunches.
- **Single Instance:** Named `Mutex` + `EventWaitHandle` for IPC (signals running instance to show UI).
- **Tray-only:** `ShutdownMode.OnExplicitShutdown`. Menu: "Open Settings", "Next Wallpaper", "Exit".
- **Wallpaper Processing:** Writes to `current.png`/`.jpg`. Title overlay via SixLabors at switch time. PNG preferred; JPEG fallback if >16 MB.
- **Favorites:** Heart toggle, stored in `settings.json`, excluded from retention cleanup.
- **Windows Startup:** Registry key at `HKCU\...\Run\PaperNexus`.

## Dependencies

Avalonia 11.3.12, CommunityToolkit.Mvvm 8.4.0, Cronos 0.11.1, Microsoft.Extensions.Hosting 10.0.3, Newtonsoft.Json 13.0.4, SixLabors.ImageSharp 3.1.12 + Drawing 2.1.7

## Settings

`Core/WallpaperNexusSettings.cs` → `%LOCALAPPDATA%\PaperNexus\settings.json`:
- `SlideshowSettings` — schedule mode, order, fill style
- `DownloadSettings` — folder path, resolution, retention days
- `List<WallpaperSource>` — name, URL, cron, enabled (default: Bing Daily)
- `AnnotationSettings` — font (Cinzel, bundled), size (18), color (#F5F5F5), position, outline (OutlineEnabled)
- `FavoriteWallpapers`, window position/size, `RunOnStartup`, `CurrentWallpaperPath`

## Code Style

Enforced via `.editorconfig`: .NET 10, C#, file-scoped namespaces, 4-space indent, CRLF, `var` everywhere, Allman braces (optional for single-line), PascalCase, explicit access modifiers, expression-bodied for properties/accessors/lambdas (not constructors), pattern matching preferred, `is null` preferred, `readonly` fields, no `this.`, `using` over `Dispose()`, no collection initializers.

**Button tooltips required:** Every `Button` in AXAML must have `ToolTip.Tip`. Tray `NativeMenuItem`s exempt.

**Suppressed diagnostics:** CS8601-CS8604, CS8618-CS8619, CA1806, CA1835, CA1848 (all `none`). `<Nullable>enable</Nullable>` is set but warnings silenced. Don't add `#nullable` annotations unless asked.

## Build & CI/CD

- **PR workflow:** restore → build (Release) → test (continue-on-error). `windows-latest`, `actions/checkout@v6`.
- **Deploy workflow:** push to `main`/tags/manual → publish win-x64 single-file → sign exe → GitHub Release.
- **Version:** Default `0.0.0`, CI sets `-p:Version=$buildNum.0.0`. Tags use `vN` format. Auto-updater compares `Version.Major` as integer.
- **Code signing:** Self-signed cert, auto-generated on first run, stored as `SIGNING_CERTIFICATE`/`SIGNING_CERTIFICATE_PASSWORD` secrets. Requires `GH_PAT` for persistence. 5-year validity, auto-renews at 30 days remaining.
- **Publishing:** `dotnet publish PaperNexus/PaperNexus.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false`
- **Actions maintenance:** 30-day cycle. **Next update: March 28, 2026.**

## Session Start Hook

`.claude/hooks/session-start.sh` installs .NET 10 SDK and restores NuGet on remote sessions (`CLAUDE_CODE_REMOTE=true`).

## Guidelines

- .NET 10.0, Avalonia UI (not WPF), root namespace `PaperNexus` / `PaperNexus.Core`
- AXAML assets: `avares://PaperNexus/Assets/...`
- New scheduled jobs: use `IScheduleScopedJob` pattern, not `ScheduledJobService`
- Never commit secrets. Check `dotnet list package --vulnerable`.
- **Update `CLAUDE.md`** after structural/pattern changes
- **No Linear issues** for this repo
- "Remember" = update this file
