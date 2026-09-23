# Temizlikci for Windows

## What This Is
The Windows 11 port of Temizlikci (`../temizlikci`, macOS): a disk analyzer with a sunburst chart and a synchronized
list that recognizes developer artifacts and Windows' own space users and labels each as Safe to Remove, Remove with
Tool, or Keep. The macOS app is the specification — when behavior is unclear, read the Swift source and its
DECISIONS.md.

## Tech Stack
| Layer | Technology | Why |
|-------|-----------|-----|
| UI | WinUI 3 (Windows App SDK 2.5), unpackaged, self-contained | Native Fluent/Mica (DECISIONS) |
| Chart | Win2D `CanvasControl` | Custom sunburst with hit testing |
| MVVM | CommunityToolkit.Mvvm | Source-generated observable properties and commands |
| Language | C# latest, .NET 10, nullable enabled, warnings as errors | |
| Interop | LibraryImport (scanner, shell, Recycle Bin COM) | |
| Tests | xUnit v3 on Microsoft.Testing.Platform | Real temp folders, synthetic MFT records |

## Architecture
`Temizlikci.App` (views, theme, composition root) → `Temizlikci.Presentation` (view models, `L10n`, formatting) →
`Temizlikci.Domain` (tree, rules, history, layout, interfaces) ← `Temizlikci.Services` (Windows implementations).
Full detail: `DEVPLAN.md`.

## Current Phase
See `.claude/phases/` — check the active phase file before starting work. The developer pre-approved running all
phases back to back (2026-09-23); commit and tag at each phase boundary.

## Rules

### Always active
@rules/common/core.md
@rules/common/decisions.md
@rules/common/git.md
@rules/common/testing.md
@rules/common/debug.md
@rules/common/existing-code.md

### UI projects only
@rules/common/frontend.md

### Language rules
@rules/dotnet/dotnet.md

## Project-Specific Constraints
- **App name is "Temizlikci"; every other user-facing string is English**, and all of them go through
  `src/Temizlikci.Presentation/Strings/L10n.cs` + `Strings.resx` (`StringCatalogTests` enforces both).
- **Never delete user content.** Removal goes through `IRecycleBin` or an owning tool. No `File.Delete` /
  `Directory.Delete` outside the app's own cache stores (`SourceHygieneTests` enforces it).
- **Never run tests or experiments that delete from real locations**, touch the real Recycle Bin, or change real WSL
  distributions. Use temporary folders and stubs.
- The app never installs a service or helper. Elevation is only ever the whole app, restarted with the user's consent.
- No inline colors, font sizes or user-facing text in XAML views — theme resources in `src/Temizlikci.App/Theme/`,
  text from `L10n` (`SourceHygieneTests` enforces both).

## Context
- Build: `dotnet build Temizlikci.slnx -c Debug`.
- Test: `dotnet test --project tests/Temizlikci.Tests` (Microsoft.Testing.Platform, opted in via `global.json`).
- Run the app: `src/Temizlikci.App/bin/Debug/net10.0-windows10.0.26100.0/win-x64/Temizlikci.exe`.
- Check a change by eye: `scripts/preview.ps1 -Out x.png [-Arguments "--theme=dark"]` starts the Debug build, captures
  its window (PrintWindow, so covering windows don't matter) and closes it. `--theme=light|dark` works in Debug only.
- Don't edit repository text files with Windows PowerShell 5.1 `Get-Content`/`Set-Content`: it reads UTF-8 without a
  BOM as ANSI and corrupts characters like "—". Use the Edit tool or Git Bash.
- Adding a user-facing string: property/method in `L10n.cs` AND a `<data>` entry (with a comment) in `Strings.resx`.
- Adding a cleanup rule: entry in `CleanupCatalog`, a reason string, and a test that it matches its target and not a
  look-alike.
