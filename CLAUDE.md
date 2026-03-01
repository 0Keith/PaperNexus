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

The release workflow signs `PaperNexus.exe` with a self-signed certificate generated inline during CI. No secrets or external services are required.

**How it works in CI:**
1. The PFX is decoded from the `SIGNING_CERTIFICATE` GitHub secret
2. `signtool.exe` (from the Windows 10 SDK bundled on `windows-latest`) signs the exe with SHA-256 digest and a DigiCert RFC 3161 timestamp
3. The PFX is deleted; the signed exe is uploaded to the GitHub Release
4. If `SIGNING_CERTIFICATE` is not set the signing step is skipped (unsigned build still succeeds)

**Required GitHub secrets** (set once in repo Settings → Secrets and variables → Actions):
- `SIGNING_CERTIFICATE` — base64-encoded PFX file
- `SIGNING_CERTIFICATE_PASSWORD` — password chosen when the PFX was exported

**One-time cert generation** (run locally on Windows, then store the output as secrets):
```powershell
$cert = New-SelfSignedCertificate `
  -Subject "CN=PaperNexus" `
  -CertStoreLocation "Cert:\CurrentUser\My" `
  -Type CodeSigningCert `
  -HashAlgorithm SHA256 `
  -NotAfter (Get-Date).AddYears(5)

$password = "your-strong-password-here"
$pfxPassword = ConvertTo-SecureString -String $password -Force -AsPlainText
$pfxPath = "$env:USERPROFILE\papernexus-sign.pfx"
Export-PfxCertificate -Certificate $cert -FilePath $pfxPath -Password $pfxPassword | Out-Null

# Copy this output into the SIGNING_CERTIFICATE secret
[Convert]::ToBase64String([IO.File]::ReadAllBytes($pfxPath))

# Then delete the local PFX and store $password in SIGNING_CERTIFICATE_PASSWORD
```

**Cert renewal:** The cert expires after 5 years. Regenerate and update both secrets before expiry. The thumbprint changes on renewal, but SmartScreen reputation is tied to the publisher name, so reputation carries forward.

**Limitations:** The cert is self-signed and not rooted in a trusted CA, so Windows SmartScreen will still warn on first run. Because the same cert is reused across every release, SmartScreen reputation accumulates over time. To eliminate warnings immediately, replace with a purchased OV/EV certificate using the same secret-based approach.

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
