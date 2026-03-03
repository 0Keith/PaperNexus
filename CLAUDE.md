# CLAUDE.md - AI Assistant Guide for PaperNexus

## Project Overview

A single-project .NET 10.0 solution providing an automated wallpaper rotation desktop app for Windows. The solution file is `PaperNexus.sln`.

## Quick Reference

```bash
# Restore packages
dotnet restore PaperNexus.sln

# Build
dotnet build PaperNexus.sln --configuration Release

# Run Paper Nexus
dotnet run --project PaperNexus

# Run tests (no dedicated test projects yet; CI uses continue-on-error)
dotnet test --configuration Release
```

## Repository Structure

```
PaperNexus/
├── PaperNexus.sln                       # Solution file
├── CLAUDE.md                            # AI assistant guide (this file)
├── .editorconfig                         # C# code style rules & diagnostics
├── .gitignore                            # Standard Visual Studio .gitignore
├── .claude/                              # Claude Code configuration
│   ├── settings.json                     # SessionStart hook
│   └── hooks/session-start.sh            # Installs .NET 10 SDK remotely
├── .github/
│   └── workflows/
│       ├── pull-request.yml              # PR build verification
│       └── deploy-wallpaper-service.yml  # Release builds + code signing
│
└── PaperNexus/                           # Main project
    ├── PaperNexus.csproj
    ├── App.axaml / App.axaml.cs          # Avalonia application root, tray icon, startup
    ├── Program.cs                         # Entry point, auto-install, single-instance mutex, IPC
    ├── AutoUpdateService.cs              # Silent auto-update via GitHub Releases + job wrapper
    ├── DownloadWallpapers.cs             # Scheduled wallpaper downloader (extends ScheduledJobService)
    ├── HttpWallpaperSourceService.cs     # HTTP feed client + WallpaperImage DTO
    ├── NativeMethods.cs                  # P/Invoke for Windows wallpaper API
    ├── SwitchWallpaper.cs                # Wallpaper switching logic + job wrapper
    ├── Assets/                            # logo.ico, logo.png
    ├── Core/                              # Shared infrastructure (inlined)
    │   ├── Bootstrapper.cs               # DI helpers, IAddSingleton<T>, AddServicesFrom()
    │   ├── Extensions.cs                 # Utility extension methods (ThrowIfNull, EnterAsync, etc.)
    │   ├── FileLogger.cs                 # File-based ILogger implementation (async queue)
    │   ├── ScheduledService.cs           # ScheduledJobService base, IScheduleScopedJob, ScheduledJobHostedService<T>
    │   └── WallpaperNexusSettings.cs     # Settings model, enums, LoadAsync/SaveAsync
    ├── ViewModels/
    │   └── WallpaperConfigViewModel.cs   # MVVM ViewModel (CommunityToolkit.Mvvm)
    └── Views/
        ├── MainWindow.axaml / .cs        # Settings window
        └── SplashScreen.axaml / .cs      # Startup splash
```

## Project: PaperNexus

- **Type:** Avalonia desktop app (WinExe), .NET 10.0, x64
- **Namespace root:** `PaperNexus` (main code), `PaperNexus.Core` (infrastructure in `Core/`)
- **Assembly name:** `PaperNexus` (produces `PaperNexus.exe`)
- **MVVM architecture** using `CommunityToolkit.Mvvm`
- **Key dependencies:**
  - Avalonia 11.3.12 (`Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`, `Avalonia.Fonts.Inter`, `Avalonia.Diagnostics` debug-only)
  - CommunityToolkit.Mvvm 8.4.0 (MVVM source generators)
  - Cronos 0.11.1 (cron scheduling)
  - Microsoft.Extensions.Hosting 10.0.3 (DI, hosted services)
  - Newtonsoft.Json 13.0.4 (JSON serialization for settings/feeds)
  - SixLabors.ImageSharp 3.1.12 + SixLabors.ImageSharp.Drawing 2.1.7 (image processing, title overlay)
- **Platform:** Windows (tray icon, registry startup, P/Invoke wallpaper API); Avalonia UI itself is cross-platform but wallpaper-setting is Windows-only at runtime
- **MSBuild properties:** `ImplicitUsings=enable`, `Nullable=enable`, `AvaloniaUseCompiledBindingsByDefault=true`

### Key Architectural Patterns

- **MVVM:** `WallpaperConfigViewModel` + `Views/MainWindow.axaml` with `CommunityToolkit.Mvvm` (`[ObservableProperty]`, `[RelayCommand]`)
- **Scheduled Jobs — Two Patterns:**
  1. **`IScheduleScopedJob` (newer):** Separate business logic interfaces (e.g., `ISwitchWallpaper`, `ICheckForUpdates`) from scheduling infrastructure. A separate job wrapper class (e.g., `SwitchWallpaperJob`, `AutoUpdateJob`) implements `IScheduleScopedJob` and delegates to the injected interface. Business logic registers as singleton via `IAddSingleton<T>`; job wrappers are auto-discovered and registered by `AddServicesFrom()` in `Bootstrapper.cs`.
  2. **`ScheduledJobService` (older base class):** `DownloadWallpapers` directly extends `ScheduledJobService` and overrides `Execute()` / `GetNextExecutionAsync()`. It is auto-discovered via `IAddHostedSingleton<IDownloadWallpapers>`, which registers it as both a singleton (accessible via `IDownloadWallpapers`) and as an `IHostedService`.
- **Silent Auto-Update:** `AutoUpdateJob` runs with `ExecuteOnStartup: true` and a daily cron (`0 3 * * *`). It delegates to `AutoUpdateService.CheckAsync()`, which queries the GitHub Releases API (`0Keith/PaperNexus`), compares the release tag's build number against `Assembly.Version.Major` as integers (tags use simplified `vN` format), downloads `PaperNexus.exe`, removes the `Zone.Identifier` ADS to avoid SmartScreen blocking, writes a self-deleting batch script to swap the file after exit, then calls `Environment.Exit(0)`. The batch script relaunches with `--updated` flag, which triggers the settings window to open. If the new exe fails to start, the batch script rolls back from the `.bak` copy.
- **Auto-Install on First Run:** When the exe runs from outside `%LOCALAPPDATA%\PaperNexus\`, `Program.Main` copies itself there, migrates `settings.json` and `timers.json` if present, and relaunches from the install location. If an installed instance is already running, it signals it to show the UI instead.
- **Single Instance + Show UI:** `Program.cs` uses a named `Mutex` (`PaperNexus_SingleInstance`) for single-instance enforcement and a named `EventWaitHandle` (`PaperNexus_ShowUI`) for IPC. When a second instance starts, it signals the running instance to show the settings window via the event handle (instead of silently exiting). `App.axaml.cs` monitors this event on a background thread and calls `ShowMainWindow()` when signaled.
- **Tray-only startup:** App runs as a system tray icon with no window on startup. `ShutdownMode.OnExplicitShutdown` keeps it alive when the settings window is closed. The tray menu provides "Open Settings", "Next Wallpaper", and "Exit".
- **Windows Startup Registration:** `UpdateStartupRegistration()` in `App.axaml.cs` writes the exe path to `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run` under the key `PaperNexus` (cleaning up old keys `Excogitated Wallpaper Service` and `Wallpaper Nexus` on first run).
- **Wallpaper Processing:** `SwitchWallpaper` writes to a fixed `current.png` (or `current.jpg`) in the app directory. Title overlay is applied at switch time (not download time) using SixLabors with `MS Gothic` font. PNG format is preferred; falls back to JPEG with quality stepping from 97% if the image exceeds 16 MB. Fill style is applied via `WallpaperStyle` and `TileWallpaper` registry keys under `HKCU\Control Panel\Desktop`.
- **Self-contained distribution:** Published as a single-file, self-contained exe.

### DI Registration Summary

In `App.OnFrameworkInitializationCompleted()`:
- `FileLoggerProvider` — added as logging provider
- `HttpWallpaperSourceService` — registered as singleton manually
- `AddServicesFrom(assembly)` — auto-discovers `IAddSingleton<T>` implementations (registers `SwitchWallpaper` as `ISwitchWallpaper`, `AutoUpdateService` as `ICheckForUpdates`), `IAddHostedSingleton<T>` implementations (registers `DownloadWallpapers` as both `IDownloadWallpapers` singleton and `IHostedService`), and `IScheduleScopedJob` implementations (registers `ScheduledJobHostedService<SwitchWallpaperJob>`, `ScheduledJobHostedService<AutoUpdateJob>`)

### Settings

`WallpaperNexusSettings` in `Core/WallpaperNexusSettings.cs` — JSON file at `{AppContext.BaseDirectory}/settings.json` (resolves to `%LOCALAPPDATA%\PaperNexus\settings.json` after install):
- `SlideshowSettings` — schedule mode (cron/interval minutes/interval hours), pattern (alphabetical/random/oldest/newest/never), fill style
- `DownloadSettings` — wallpapers folder path (default: `%USERPROFILE%\Pictures\PaperNexus`), resolution, retention days
- `List<WallpaperSource>` — name, URL, cron expression, enabled flag; default: Bing Daily via peapix.com
- Window position/size persistence (`WindowX`, `WindowY`, `WindowWidth`, `WindowHeight`)
- `AnnotateWallpaper`, `RunOnStartup`, `CurrentWallpaperPath`

## Code Style & Conventions

All rules are enforced via `.editorconfig`. Key conventions:

- **Target framework:** .NET 10.0
- **Language:** C# with file-scoped namespaces (`csharp_style_namespace_declarations = file_scoped`)
- **Indentation:** 4 spaces, CRLF line endings
- **`var` usage:** Preferred everywhere
- **Braces:** Allman style (new line before all opening braces), optional for single-line blocks
- **Naming:** PascalCase for types/methods/properties, `I` prefix for interfaces
- **Modifiers:** Explicit access modifiers required; preferred order: `public, private, protected, internal, static, extern, new, virtual, abstract, sealed, override, readonly, unsafe, volatile, async`
- **Expression-bodied members:** Yes for properties/accessors/lambdas/local functions, no for constructors/operators
- **Pattern matching:** Preferred over `is`/`as` casts
- **Null checks:** `is null` preferred over reference equality
- **Fields:** `readonly` preferred
- **No `this.` qualification**
- **`using` over manual `Dispose()`:** Prefer `using` statements/declarations over calling `.Dispose()` directly
- **Collection/object initializers:** Disabled (silent — don't suggest)
- **Button tooltips:** Every `Button` in AXAML must have a `ToolTip.Tip` attribute. For buttons created in code-behind, use `ToolTip.SetTip(button, "...")`. This applies to all views; tray `NativeMenuItem`s are exempt (OS menus don't support tooltips).

### Suppressed Diagnostics

Nullable reference type warnings are globally suppressed via `.editorconfig`:
- CS8601, CS8602, CS8603, CS8604, CS8618, CS8619 — all set to `severity: none`
- CA1806 (ignore method results), CA1835 (Memory-based overloads), CA1848 (LoggerMessage delegates) — also suppressed

Note: `<Nullable>enable</Nullable>` is set in the csproj (enabling the compiler analysis), but the above diagnostics are silenced. Do not add `#nullable` annotations or fix nullable warnings unless explicitly asked.

## Build & CI/CD

### GitHub Actions Workflows

1. **`pull-request.yml`** — Runs on PRs targeting `main`: restore, build (Release), test (continue-on-error). Read-only permissions.
2. **`deploy-wallpaper-service.yml`** — Triggered on push to `main`, version tags (`v*`), or `workflow_dispatch` (manual): builds win-x64 self-contained single-file publish, signs exe with a self-signed certificate, creates GitHub Release. Write permissions for contents.

All workflows run on `windows-latest` and use `actions/checkout@v6`.

### Version Strategy

- Default version: `0.0.0` in csproj `<Version>`
- CI override: `-p:Version=$buildNum.0.0` where `$buildNum` is either the number from a `vN` git tag or `{run_number}`
- GitHub release tags use simplified `vN` format (e.g., `v69` not `v1.0.69`)
- Runtime access: `Assembly.GetExecutingAssembly().GetName().Version.Major` formatted as `v{Major}` in `App.AppVersion`
- Auto-updater compares build numbers as integers via `Version.Major` (not full `System.Version` objects)

### Code Signing (self-signed certificate)

The release workflow signs `PaperNexus.exe` with a persistent self-signed certificate. The cert is auto-generated on the first run and stored back as repository secrets so every subsequent release is signed with the same identity, allowing SmartScreen reputation to accumulate.

**How it works in CI:**
1. If `SIGNING_CERTIFICATE` secret exists, the PFX is decoded and its expiry is checked
2. If the cert is expired or expires within 30 days, a new cert is generated automatically and stored back to secrets (requires `GH_PAT`)
3. Otherwise the stored cert is reused as-is
4. If no `SIGNING_CERTIFICATE` secret exists, a new cert is generated on first run and stored via `gh secret set`
5. `signtool.exe` (Windows 10 SDK, bundled on `windows-latest`) signs the exe with SHA-256 digest and a DigiCert RFC 3161 timestamp
6. The PFX is deleted; the signed exe is uploaded to the GitHub Release

**One-time setup** (only required to enable auto-persistence on first run):
Add a single secret in repo Settings > Secrets and variables > Actions:
- `GH_PAT` — a fine-grained PAT with **Read and write** access to **Secrets** for this repository

On the first workflow run with `GH_PAT` set, the cert is generated automatically and `SIGNING_CERTIFICATE` / `SIGNING_CERTIFICATE_PASSWORD` are written back. `GH_PAT` is the only secret you ever need to add manually.

Without `GH_PAT`, signing still works but the cert is ephemeral per-run (no reputation accumulation).

**Cert renewal:** The cert is valid for 5 years. The workflow automatically regenerates the cert when it has 30 or fewer days remaining, storing the new cert back to `SIGNING_CERTIFICATE` / `SIGNING_CERTIFICATE_PASSWORD` (requires `GH_PAT`). To force immediate regeneration, delete the `SIGNING_CERTIFICATE` and `SIGNING_CERTIFICATE_PASSWORD` secrets — the next run will create a fresh cert. SmartScreen reputation is tied to the publisher name (`CN=PaperNexus`), so it carries forward after renewal.

**Limitations:** Self-signed certs are not rooted in a trusted CA, so SmartScreen warns on first download. Reputation accumulates across releases as long as the same cert is reused. To eliminate warnings immediately, replace with a purchased OV/EV certificate stored using the same `SIGNING_CERTIFICATE` / `SIGNING_CERTIFICATE_PASSWORD` secret names.

### GitHub Actions Maintenance

Actions versions are updated on a 30-day cycle. **Next update due: March 28, 2026.**

### Publishing

```bash
# Self-contained single-file publish
dotnet publish PaperNexus/PaperNexus.csproj \
  -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:PublishTrimmed=false
```

## Testing

No dedicated test projects exist yet. The PR workflow runs `dotnet test` with `continue-on-error: true`. When adding tests, use the standard .NET test project pattern and ensure they are discoverable by `dotnet test` at the solution level.

## Session Start Hook

The `.claude/hooks/session-start.sh` script runs when Claude Code starts a remote session (`CLAUDE_CODE_REMOTE=true`). It installs the .NET 10 SDK via `apt-get` and restores NuGet packages for the solution.

## Memory Convention

When the user asks you to "remember" something, that means update `CLAUDE.md` with the relevant information so it persists across sessions.

## Guidelines for AI Assistants

- Use .NET 10.0 for all new development
- Follow the `.editorconfig` conventions strictly — do not override suppressed diagnostics
- Use Avalonia UI (not WPF) for any new UI work
- Root namespace for new files: `PaperNexus` (or `PaperNexus.Core` for infrastructure in `Core/`)
- Asset references in AXAML use `avares://PaperNexus/Assets/...`
- Maintain Windows compatibility for wallpaper/tray/registry features
- Keep self-contained, single-file executables for distribution
- For new scheduled jobs, prefer the `IScheduleScopedJob` pattern with a separate job wrapper class; avoid extending `ScheduledJobService` directly
- Never commit secrets or credentials
- Check for vulnerable packages with `dotnet list package --vulnerable`
- **Always update `CLAUDE.md`** after any task that changes the project structure, adds patterns, or migrates code
- **No Linear issues exist for this repo** — do not search Linear for related issues
