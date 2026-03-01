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
├── .editorconfig                         # C# code style rules
├── .claude/                              # Claude Code configuration
│   ├── settings.json                     # SessionStart hook
│   └── hooks/session-start.sh            # Installs .NET 10 SDK remotely
├── .github/
│   └── workflows/
│       ├── pull-request.yml              # PR build verification
│       └── deploy-wallpaper-service.yml  # Release builds
│
└── PaperNexus/                           # Main project
    ├── PaperNexus.csproj
    ├── App.axaml / App.axaml.cs          # Avalonia application root
    ├── Program.cs                         # Entry point, global usings
    ├── AutoUpdateService.cs              # Silent auto-update via GitHub Releases
    ├── DownloadWallpapers.cs             # Scheduled wallpaper downloader
    ├── HttpWallpaperSourceService.cs     # HTTP feed client
    ├── NativeMethods.cs                  # P/Invoke for Windows wallpaper API
    ├── SwitchWallpaper.cs                # Wallpaper switching logic + job wrapper
    ├── Assets/                            # logo.ico, logo.png
    ├── Core/                              # Shared infrastructure (inlined)
    │   ├── Bootstrapper.cs               # DI helpers, IAddSingleton<T>
    │   ├── Extensions.cs                 # Utility extension methods
    │   ├── FileLogger.cs                 # File-based ILogger implementation
    │   ├── ScheduledService.cs           # IScheduleScopedJob, ScheduledJobHostedService
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
- **Key dependencies:** Cronos (scheduling), SixLabors.ImageSharp (image processing), Avalonia 11.3.12, Newtonsoft.Json, Microsoft.Extensions.Hosting
- **Platform:** Windows (tray icon, registry startup, P/Invoke wallpaper API); Avalonia UI itself is cross-platform but wallpaper-setting is Windows-only at runtime

### Key Architectural Patterns

- **MVVM:** `WallpaperConfigViewModel` + `Views/MainWindow.axaml` with `CommunityToolkit.Mvvm` (`[ObservableProperty]`, `[RelayCommand]`)
- **Scheduled Jobs Pattern:** Separate business logic interfaces (e.g., `ISwitchWallpaper`) from scheduling infrastructure (`IScheduleScopedJob`). Business logic implements its domain interface only. A separate job wrapper class implements `IScheduleScopedJob` and delegates to the injected interface. Register business logic as singleton via `IAddSingleton<T>`; use `AddServicesFrom()` for automatic registration.
- **Silent Auto-Update:** `AutoUpdateService` implements `IScheduleScopedJob` with `ExecuteOnStartup: true` and a daily cron (`0 3 * * *`). It queries the GitHub Releases API (`0Keith/PaperNexus`), compares the release tag version against the embedded assembly version (`<Version>` in csproj, overridden by git tag via `-p:Version=` in CI), downloads `PaperNexus.exe`, writes a self-deleting batch script to swap the file after exit, then calls `Environment.Exit(0)`. The deploy workflow creates a public GitHub Release when a `v*` tag is pushed.
- **Tray-only startup:** App runs as a system tray icon with no window on startup. `ShutdownMode.OnExplicitShutdown` keeps it alive when the settings window is closed.
- **Windows Startup Registration:** `RegisterStartup()` in `App.axaml.cs` writes the exe path to `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run` under the key `PaperNexus` (cleaning up old keys `Excogitated Wallpaper Service` and `Wallpaper Nexus` on first run).
- **Self-contained distribution:** Published as a single-file, self-contained exe.

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
- **Collection/object initializers:** Disabled (silent — don't suggest)

### Suppressed Diagnostics

Nullable reference type warnings are globally suppressed:
- CS8601, CS8602, CS8603, CS8604, CS8618, CS8619 — all set to `severity: none`
- CA1806 (ignore method results), CA1835 (Memory-based overloads), CA1848 (LoggerMessage delegates) — also suppressed

Do not add `#nullable` annotations or fix nullable warnings unless explicitly asked.

## Build & CI/CD

### GitHub Actions Workflows

1. **`pull-request.yml`** — Runs on all PRs: restore, build (Release), test (continue-on-error)
2. **`deploy-wallpaper-service.yml`** — Triggered on push to master/version tags/manual: builds win-x64 self-contained single-file publish, signs exe with a self-signed certificate, creates GitHub Release

All workflows run on `windows-latest` and use `actions/checkout@v6`.

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
Add a single secret in repo Settings → Secrets and variables → Actions:
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

## Guidelines for AI Assistants

- Use .NET 10.0 for all new development
- Follow the `.editorconfig` conventions strictly — do not override suppressed diagnostics
- Use Avalonia UI (not WPF) for any new UI work
- Root namespace for new files: `PaperNexus` (or `PaperNexus.Core` for infrastructure in `Core/`)
- Asset references in AXAML use `avares://PaperNexus/Assets/...`
- Maintain Windows compatibility for wallpaper/tray/registry features
- Keep self-contained, single-file executables for distribution
- Never commit secrets or credentials
- Check for vulnerable packages with `dotnet list package --vulnerable`
- **Always update `CLAUDE.md`** after any task that changes the project structure, adds patterns, or migrates code
