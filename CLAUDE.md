# CLAUDE.md - AI Assistant Guide for Excogitated.AppManager

## Project Overview

A multi-project .NET 10.0 solution containing a cross-platform civilization simulator (ProjectGenesis), an automated wallpaper rotation desktop app (Wallpaper Nexus), and a cross-platform app launcher utility. The solution file is `Excogitated.Projects.sln`.

## Quick Reference

```bash
# Restore all packages
dotnet restore Excogitated.Projects.sln

# Build entire solution
dotnet build Excogitated.Projects.sln --configuration Release

# Run ProjectGenesis UI
dotnet run --project ProjectGenesis.UI

# Run Wallpaper Nexus
dotnet run --project Excogitated.WallpaperNexus

# Run tests (no dedicated test projects yet; CI uses continue-on-error)
dotnet test --configuration Release
```

## Repository Structure

```
Excogitated.AppManager/
├── Excogitated.Projects.sln                 # Solution file (6 projects)
├── .editorconfig                             # C# code style rules
├── .claude/                              # Claude Code configuration
│   ├── settings.json                     # SessionStart hook
│   └── hooks/session-start.sh            # Installs .NET 10 SDK remotely
├── .github/
│   ├── copilot-instructions.md           # Copilot guidelines
│   └── workflows/
│       ├── pull-request.yml              # PR build verification
│       ├── deploy-project-genesis.yml    # Genesis release builds
│       └── deploy-wallpaper-service.yml  # Wallpaper release builds
│
├── ProjectGenesis/                       # Core simulation engine (class library)
├── ProjectGenesis.UI/                    # Avalonia desktop frontend
├── Excogitated.WallpaperNexus/         # Wallpaper Nexus (Avalonia desktop)
├── Excogitated.Core/                     # Shared utilities library
├── Excogitated.Service/                  # Windows service / console app
└── Excogitated.AppLauncher/              # App launcher utility (Avalonia, cross-platform)
```

## Projects

### ProjectGenesis (Core Engine)
- **Type:** Class library, .NET 10.0
- **Purpose:** Text-driven civilization simulator with deterministic data-oriented design
- **Key patterns:** `ILegislationModule` interface, reflection-based `ModuleRegistry`, `WorldState`, `SimulationEngine`
- **No external NuGet dependencies** (self-contained)
- **Module location:** `ProjectGenesis/Modules/Concrete/`
- **Adding a module:** Implement `ILegislationModule`, place in `Modules/Concrete/` — auto-discovered via reflection

### ProjectGenesis.UI (Frontend)
- **Type:** Avalonia desktop app, .NET 10.0
- **MVVM architecture** using `CommunityToolkit.Mvvm`
- **Cross-platform:** Windows, Linux, macOS
- **References:** ProjectGenesis

### Excogitated.WallpaperNexus (Wallpaper Nexus)
- **Type:** Avalonia desktop app, .NET 10.0
- **MVVM architecture** using `CommunityToolkit.Mvvm`
- **Key dependencies:** Cronos (scheduling), SixLabors.ImageSharp (image processing), Avalonia 11.3.12
- **References:** Excogitated.Core

### Excogitated.Core (Shared Library)
- **Type:** Class library, .NET 10.0
- **Contents:** Extensions, FileLogger, ScheduledService base class, settings models
- **Dependencies:** Microsoft.Extensions.Hosting.Abstractions, Newtonsoft.Json

### Excogitated.Service
- **Type:** Console app (Windows Service), .NET 10.0, x64
- **Dependencies:** Microsoft.Extensions.Hosting.WindowsServices, ML.AutoML, ImageSharp
- **References:** Excogitated.Core

### Excogitated.AppLauncher
- **Type:** Avalonia desktop app, .NET 10.0 (cross-platform; converted from WPF Feb 2026)
- **Purpose:** Reflection-based utility launcher — discovers all `IExecutable` implementations at startup, groups them by `IExecutable.Group`, and presents them as buttons
- **Key patterns:** `IExecutable` interface, `LocalBus` event bus (`IConsume<T>`), `Debouncer<T>` for progress message throttling
- **Structure:** `Views/MainWindow.axaml`, `App.axaml`, `Program.cs`, `Bus/`, `Messages/`, `Apps/`
- **Adding a launcher action:** Implement `IExecutable` in `Apps/` — auto-discovered via reflection
- **Note:** `Apps/DeleteCurrentWallpaper.cs` uses Windows P/Invoke (`user32.dll`) and only works on Windows at runtime

## Code Style & Conventions

All rules are enforced via `.editorconfig`. Key conventions:

- **Target framework:** .NET 10.0 for all new code
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
2. **`deploy-project-genesis.yml`** — Triggered on push to master/version tags/manual: builds win-x64 self-contained single-file publish
3. **`deploy-wallpaper-service.yml`** — Same triggers, publishes Wallpaper Nexus

All workflows run on `windows-latest` and use `actions/checkout@v6` and `actions/upload-artifact@v6`.

### GitHub Actions Maintenance

Actions versions are updated on a 30-day cycle. See `.github/copilot-instructions.md` for the schedule and process. **Next update due: March 28, 2026.**

### Publishing

```bash
# Self-contained single-file publish (example for ProjectGenesis.UI)
dotnet publish ProjectGenesis.UI/ProjectGenesis.UI.csproj \
  -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:PublishTrimmed=false
```

## Testing

No dedicated test projects exist yet. The PR workflow runs `dotnet test` with `continue-on-error: true`. When adding tests, use the standard .NET test project pattern and ensure they are discoverable by `dotnet test` at the solution level.

## Session Start Hook

The `.claude/hooks/session-start.sh` script runs when Claude Code starts a remote session (`CLAUDE_CODE_REMOTE=true`). It installs the .NET 10 SDK via `apt-get` and restores NuGet packages for the solution.

## Key Architectural Patterns

- **Data-Oriented Design:** ProjectGenesis uses deterministic simulation with `deltaTime` passed to all module `Execute()` calls
- **Reflection-based Module Discovery:** `ModuleRegistry` auto-discovers all `ILegislationModule` implementations at startup
- **MVVM:** Avalonia apps use ViewModels + Views with `CommunityToolkit.Mvvm` (`[ObservableProperty]`, `[RelayCommand]`)
- **Cross-platform UI:** New projects use Avalonia UI (not WPF) for Windows/Linux/macOS support
- **Self-contained distribution:** All apps are published as single-file, self-contained executables
- **Scheduled Jobs Pattern:** Separate business logic interfaces (e.g., `ISwitchWallpaper`) from scheduling infrastructure (`IScheduleScopedJob`). Business logic classes implement their domain interface only. Separate job wrapper classes implement `IScheduleScopedJob` and delegate to the injected business logic interface. Register business logic as singleton and use `AddScheduledJobs()` for automatic job registration.
- **Silent Auto-Update (Wallpaper Nexus):** `AutoUpdateService` implements `IScheduleScopedJob` with `ExecuteOnStartup: true` and a daily cron (`0 3 * * *`). It queries the GitHub Releases API, compares the release tag version against the embedded assembly version (`<Version>` in csproj, overridden by git tag via `-p:Version=` in CI), downloads the new exe, writes a self-deleting batch script to perform the file swap after exit, then calls `Environment.Exit(0)`. The deploy workflow creates a public GitHub Release (no auth required to download) when a `v*` tag is pushed.

## Documentation Map

| File | Purpose |
|------|---------|
| `README.md` | Main project overview and quick links |
| `README_PROJECT_GENESIS.md` | Architecture deep-dive |
| `ProjectGenesis/README.md` | Engine documentation with examples |
| `ProjectGenesis.UI/README.md` | UI guide and system requirements |
| `QUICKSTART.md` | 5-minute getting started |
| `BUILD_WINDOWS.md` | Windows build with Visual Studio |
| `DEPLOYMENT.md` | Release process, semantic versioning |
| `DOWNLOAD.md` | User download/install guide |
| `RELEASES.md` | Version history |
| `.github/copilot-instructions.md` | Copilot/AI guidelines, maintenance schedule |

## WPF → Avalonia Conversion Reference

Key API mappings used when converting `Excogitated.AppLauncher` (Feb 2026):

| WPF | Avalonia |
|-----|----------|
| `Dispatcher.InvokeAsync(...)` | `Dispatcher.UIThread.InvokeAsync(...)` |
| `Label.Content = value` | `TextBlock.Text = value` |
| `ListView` | `ListBox` |
| `listView.ScrollIntoView(item)` | `listBox.ScrollIntoView(listBox.ItemCount - 1)` |
| `HorizontalContentAlignment` on `ItemsControl` | Set `HorizontalAlignment = Stretch` on each child control |
| `App.xaml` / `App.xaml.cs` | `App.axaml` / `App.axaml.cs` |
| `MainWindow.xaml` / `.xaml.cs` | `Views/MainWindow.axaml` / `.axaml.cs` |
| `xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"` | `xmlns="https://github.com/avaloniaui"` |
| `StartupUri="MainWindow.xaml"` | Set `desktop.MainWindow = new MainWindow()` in `App.axaml.cs` |
| `DispatcherUnhandledException` in `App` | `TaskScheduler.UnobservedTaskException` + `AppDomain.CurrentDomain.UnhandledException` in `Program.cs` |
| `[assembly: ThemeInfo(...)]` in `AssemblyInfo.cs` | Not needed — delete |
| `net10.0-windows` + `<UseWPF>true</UseWPF>` | `net10.0` + Avalonia NuGet packages |
| `System.Windows.RoutedEventArgs` | `Avalonia.Interactivity.RoutedEventArgs` |
| `HorizontalAlignment.Stretch` | `Avalonia.Layout.HorizontalAlignment.Stretch` |
| `GroupBox` (`System.Windows.Controls`) | `HeaderedContentControl` (`Avalonia.Controls.Primitives`) — add `using Avalonia.Controls.Primitives;` |
| `[STAThread]` on `Main` | Remove — not available/needed in `net10.0` Avalonia apps |

**Note:** `GroupBox` does **not** exist in Avalonia 11.x. Use `HeaderedContentControl` from `Avalonia.Controls.Primitives`. Both `Header` and `Content` properties are present and `IsEnabled` is inherited from `Control`. The `xmlns="https://github.com/avaloniaui"` AXAML namespace resolves `<HeaderedContentControl>` automatically.

**Note:** Without `<ImplicitUsings>enable</ImplicitUsings>` in the csproj, `System` types (`AppDomain`, `TaskScheduler`, etc.) need explicit `using System;` / `using System.Threading.Tasks;` in `Program.cs`.

`Dispatcher.UIThread.InvokeAsync(Func<Task>)` handles async lambdas correctly — single `await` needed (no double-await pattern required).

## Guidelines for AI Assistants

- Use .NET 10.0 for all new development
- Follow the `.editorconfig` conventions strictly — do not override suppressed diagnostics
- Prefer Avalonia UI over WPF for any new UI work (cross-platform)
- When creating simulation modules, implement `ILegislationModule` and place in `ProjectGenesis/Modules/Concrete/`
- Use `world.ApplyRate()` for deterministic state changes in modules
- Maintain cross-platform compatibility (Windows, Linux, macOS)
- Keep self-contained, single-file executables for distribution
- Never commit secrets or credentials
- Check for vulnerable packages with `dotnet list package --vulnerable`
- Update `RELEASES.md` when shipping new features
- **Always update `CLAUDE.md`** after any task that changes the project structure, adds patterns, or converts/migrates code
