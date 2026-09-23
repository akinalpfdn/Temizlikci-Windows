# Temizlikci for Windows — Development Plan

## Overview
A native Windows 11 port of Temizlikci, the macOS disk analyzer (`../temizlikci`). It shows what uses disk space
folder by folder, as an interactive sunburst chart paired with a sortable list, and helps developers reclaim space
safely: it recognizes developer artifacts and Windows' own space users (WinSxS, Windows Update downloads, WSL and
Docker disks, `node_modules`, Gradle caches, `bin`/`obj`…) and labels each one **Safe to Remove**, **Remove with
Tool**, or **Keep**.

The macOS app is the specification. Behavior, safety rules, and wording carry over; only platform mechanics change.
The app name is **Temizlikci**. Every other user-facing string is **English**.

## Platform & Stack
- Target: Windows 11 (build 22621+), x64. Unpackaged desktop app, self-contained Windows App SDK.
- Toolchain: .NET 10 SDK, C# (latest), `dotnet` CLI only — no Visual Studio required.
- UI: WinUI 3 (Windows App SDK 2.5). Mica backdrop, custom title bar with a menu bar.
- Chart: Win2D `CanvasControl`, custom sunburst renderer with pure, tested layout and hit testing.
- MVVM: CommunityToolkit.Mvvm in a UI-framework-free Presentation library, so view models are unit-tested.
- Interop: `LibraryImport` for the scanner's hot paths (NtQueryDirectoryFile, raw volume reads); CsWin32 for shell COM
  (IFileOperation for the Recycle Bin).
- Tests: xUnit v3. Scanner, MFT parser, rules, and history tested against real temporary folders or synthetic bytes.
- Persistence: `%LOCALAPPDATA%\Temizlikci` — Brotli-compressed scan archives and snapshots, JSON settings.

## Architecture
```
Temizlikci.App (WinUI 3)        Views, theme resources, Win2D chart control, composition root
        │
Temizlikci.Presentation         View models (ObservableObject), L10n + Strings.resx, formatting
        │
Temizlikci.Domain               FileNode tree, cleanup rules, rule engine, history, layout, parsers,
        ▲                       service interfaces (IDiskScanner, IRecycleBin, IToolRunner, …)
        │
Temizlikci.Services             Windows implementations: directory scanner, MFT scanner, Recycle Bin,
                                volume info, elevation, Git, WSL, editors, caches, update check
```
Dependencies point inward: Presentation and Services depend on Domain only; the App composes everything.

## Platform Mapping (macOS → Windows)
| macOS | Windows |
|---|---|
| Full Disk Access probe + banner | Elevation check + "Restart as Administrator" banner; admin enables SeBackupPrivilege and the MFT scanner |
| `FileManager.contentsOfDirectory` walk | `NtQueryDirectoryFile(FileIdFullDirectoryInformation)`: allocation size + file ID per entry, no per-file open |
| — | Admin + NTFS: read the Master File Table directly (seconds for millions of files) |
| Firmlinks / volume roots skipped | Reparse points that are name surrogates (junctions, symlinks, mount points) never followed; cloud placeholders are |
| Hard links by inode | Hard links by NTFS file ID |
| Trash + Put Back | Recycle Bin via `IFileOperation` + Put Back of the `$R` item |
| APFS container breakdown (`diskutil`) | NTFS metadata ($MFT), shadow copies, page/hibernation files, unreadable folders, remainder |
| `simctl` (simulators) | `wsl` (distributions: compact, unregister), DISM component cleanup, Storage Settings |
| Xcode / Android Studio / VS Code | Visual Studio / Android Studio / VS Code |
| Apple Intelligence folder explanation | Not available (no general on-device model API for unpackaged apps) — deterministic identification only |
| Finder reveal / Quick Look | Explorer select / Properties dialog |
| UserDefaults | `settings.json` |
| LZFSE | Brotli |

## Constraints
- **Nothing is deleted.** Removal goes through the Recycle Bin (undoable) or the owning tool. Never `File.Delete` or
  `Directory.Delete` on user content. Destructive paths are tested on temp fixtures only.
- **Running as administrator widens the blast radius.** Windows has no SIP, so the app itself protects system folders:
  `C:\Windows`, `Program Files`, `ProgramData`, the root of a drive, page/hibernation/swap files and anything labelled
  Keep or Remove with Tool can never be moved to the Recycle Bin from the app.
- **Sizes are allocated sizes**; hard links count once; reparse points are never followed.
- **Tests never touch real user folders, the real Recycle Bin, or real WSL distributions.**
- The app never installs a service or a helper.

## Design Direction
"A calm, native Windows 11 utility: Mica chrome, solid white / near-black content surfaces, the validated sunburst
palette as the only saturated color, dense but legible like a developer tool."
- Chrome (title bar, sidebar) sits on Mica; chart and list sit on a solid content surface (#FFFFFF / #1E1E1E), the
  surfaces the macOS palette was validated on.
- The sunburst is paired with a sortable, searchable list. The selected item's name, size, and share are always
  visible as text. Meaning is never carried by color alone (symbols, labels, separators, hatching).
- Every toolbar action is also in the menu bar with a Windows-standard shortcut.
- Light, Dark, and High Contrast follow the system.

## Phases
Phase files live in `.claude/phases/`. The developer pre-approved running all phases back to back (2026-09-23),
committing and tagging at each phase boundary.

| # | Phase | Delivers |
|---|---|---|
| 001 | WIN-01 Foundation | Solution, projects, theme tokens, strings, app shell, test project |
| 002 | WIN-02 Domain | FileNode, rules + Windows catalog, history, layout, archive, parsers — with tests |
| 003 | WIN-03 Directory scanner | NtQueryDirectoryFile scanner, volume info, progress, cancellation — with fixture tests and measurements |
| 004 | WIN-04 MFT scanner & elevation | Elevation, SeBackupPrivilege, MFT reader, scanner selection — with parser tests and live comparison |
| 005 | WIN-05 Overview UI | Sunburst, list, breadcrumb, inspector, navigation, search, progress, keyboard |
| 006 | WIN-06 Actions & persistence | Recycle Bin + Undo + Put Back, scan cache, settings, access banner |
| 007 | WIN-07 Developer insights | Developer view, highlight, WSL / DISM / Storage tools, Other Used Space breakdown |
| 008 | WIN-08 History & projects | What Grew, Large Files, Stale Projects + Git, editors, "What Is This?" |
| 009 | WIN-09 Polish & release | Intro, accessibility, update check, icon, publish script, README |

## Implementation Guidelines
- SOLID; services behind interfaces and injected; nothing creates its own dependencies except the composition root.
- No hardcoded user-facing strings (all through `L10n` + `Strings.resx`, test-enforced). No inline colors, fonts or
  spacing in views (theme resources only).
- View models never block the UI thread: scanning, rule matching, archive I/O and tool runs happen on the thread pool.
- Every stored `CancellationTokenSource` is cancelled on teardown.
- Errors are typed and specific; no silent catches except where a comment explains why (caches, history).
- Test names: `Should_X_When_Y`.
