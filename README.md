<h1 align="center">Temizlikci for Windows</h1>

<p align="center">
  <strong>See what's filling your PC. Clean up what's safe. Keep what isn't.</strong><br>
  A native Windows 11 disk analyzer that knows what developer files are and which ones you can delete.
</p>

<p align="center">
  <a href="https://github.com/akinalpfdn/Temizlikci-Windows/releases/latest/download/Temizlikci-Setup.exe">
    <img alt="Download for Windows" src="https://img.shields.io/badge/Download_for_Windows-0078D4?style=for-the-badge&logo=windows11&logoColor=white" height="44">
  </a>
  <br>
  <sub>Free and open source · Windows 11 · x64 · No administrator rights needed to install</sub>
</p>

<p align="center">
  <img alt="Windows 11" src="https://img.shields.io/badge/Windows-11-0078D4">
  <img alt=".NET 10" src="https://img.shields.io/badge/.NET-10-512BD4">
  <img alt="WinUI 3" src="https://img.shields.io/badge/WinUI-3-0078D4">
  <img alt="MIT License" src="https://img.shields.io/badge/license-MIT-blue">
</p>

<p align="center">
  <img alt="Temizlikci showing the C: drive as a sunburst chart next to a sortable list with the change since the last scan, and the inspector describing the drive" src="assets/images/overview.png">
</p>

<p align="center"><sub>Also on the Mac: <a href="https://github.com/akinalpfdn/Temizlikci">Temizlikci for macOS</a>.</sub></p>

---

## Why

A developer's PC fills itself up: `bin` and `obj` folders in every project, `node_modules`, Gradle, NuGet and Cargo
caches, Android emulators and system images, Docker's and WSL's virtual disks that only ever grow, and Windows' own
update leftovers.

General disk tools show you *that* a folder is big. They can't tell you whether it's build output the next build
recreates, an emulator you should delete in Android Studio, a WSL disk that needs compacting, or the component store
only DISM may touch. So you either leave the space alone or delete things and hope.

Temizlikci shows where the space went and labels every developer file it recognizes. Files only ever go to the Recycle
Bin, where you can put them back; everything else is removed through the tool that owns it, after you confirm.

## What it does

### See where your space went
- **Sunburst chart and a synchronized list.** Click into any folder; the chart and the sortable, searchable list follow
  each other. Arrow keys, Enter and Escape work on the chart, and screen readers can read its segments.
- **Other Used Space, explained.** On a whole drive, the space no folder accounts for is split into what it is: NTFS's
  own files, restore points and shadow copies, and what remains — never a made-up number.
- **What Grew.** Every scan leaves a small summary. The next scan tells you what grew and what shrank, and the list
  shows the change next to every folder.
- **Large Files.** The biggest individual files on the drive, one click from the chart or Explorer.

<p align="center">
  <img alt="A projects folder opened in the chart, with the list and the inspector offering Move to Recycle Bin" src="assets/images/folder.png">
</p>

<p align="center">
  <img alt="What Grew listing the biggest changes since the previous scan" src="assets/images/what-grew.png" width="49%">
  <img alt="Large Files listing the biggest individual files on the drive" src="assets/images/large-files.png" width="49%">
</p>

### Clean up without guessing
- **63 rules** for Windows itself, Visual Studio, .NET, WSL, Docker, Android, Flutter and Dart, Node (npm, Yarn, pnpm,
  Bun), Rust, Go, Python, Java and Gradle, Unity and editors.
- **Three labels, each with a reason:**

  | Label | Meaning | Examples |
  |---|---|---|
  | **Safe to Remove** | The tool rebuilds it | `bin` and `obj`, `node_modules`, Gradle caches, Rust `target` |
  | **Remove with Tool** | Deleting it by hand breaks something; Temizlikci opens the right tool | WinSxS (DISM), WSL disks, Windows Update downloads (Storage Settings), NuGet packages (`dotnet nuget locals`), Android emulators (Android Studio) |
  | **Keep** | Windows needs it, or it's your data | the page file, the Windows Installer cache, Store apps' data |

- **Nothing is deleted.** Items go to the Recycle Bin, with Undo and Put Back. Anything labelled *Keep* or *Remove with
  Tool* — and anything inside it — can't be moved from Temizlikci at all. The Windows folder and the drive roots are
  never offered; items inside Program Files and ProgramData move only after a confirmation that suggests uninstalling
  the program instead. There is no "clean everything" button, on purpose.
- **Windows' own tools, properly.** Compact a WSL disk or remove a distribution, clean up the component store with
  DISM, and open Storage Settings, System Protection or Android Studio — each after a confirmation that says what will
  happen. Docker Desktop's own distributions are left to Docker.

<p align="center">
  <img alt="Developer Files: space by safety label, and groups for Windows, Visual Studio, .NET, Docker, Android, Flutter, Node.js, Rust, Go, Python, Java and Unity with what each can reclaim" src="assets/images/developer.png">
</p>

### Made for developers
- **Stale projects.** Projects nobody has touched for 30 days to a year, with the build output they still hold.
  "Last worked on" comes from Git, not from folder dates, which change when tools run.
- **Unpushed work warnings.** Before you delete an old project, Temizlikci tells you what exists only on this PC:
  uncommitted and untracked files, stashes, a missing remote, and commits no remote has — on every branch. It reads Git
  without writing anything, so looking at a project never changes its date, and a repository's own settings never get
  to run a program.
- **Open in your editor.** Visual Studio (the solution), Android Studio, or Visual Studio Code, straight from the
  project.

<p align="center">
  <img alt="Stale Projects: Git repositories with Pushed or Unpushed Work badges, when each was last worked on, and the build output each holds" src="assets/images/stale-projects.png">
</p>

### And it's quick about it
- **Opens instantly.** The last scan is saved and shown at launch, then refreshed in the background once it's older
  than you choose.
- **Grows while it scans.** Large folders fill the chart as they're read. A whole 930 GB drive with 3.4 million files
  took 2 min 20 s without administrator rights on the author's PC, and the window stays responsive throughout.
- **Reads the Master File Table when it can.** Run as administrator, Temizlikci measures a drive from NTFS's own index
  instead of folder by folder — more than twice as fast — and reads the folders Windows keeps from regular accounts.
- **"What is this?"** Temizlikci identifies Windows' known folders, the app behind a folder in AppData or a Store
  package, and file types.

## Privacy

- No analytics, no accounts, no tracking.
- The only network request is a check for new versions on GitHub, at most once a day. It sends nothing about your PC
  or your files, and you can turn it off in Settings.
- Scans only read names, sizes and dates. Nothing is changed or deleted while it scans.

## Install

1. [Download Temizlikci-Setup.exe](https://github.com/akinalpfdn/Temizlikci-Windows/releases/latest/download/Temizlikci-Setup.exe)
   — always the latest version. Older versions, release notes and a portable zip are on the
   [Releases](https://github.com/akinalpfdn/Temizlikci-Windows/releases) page.
2. Run it. Temizlikci installs for your account only, with no administrator rights, and appears in the Start menu.
   Nothing else needs to be installed: .NET and the Windows App SDK are included.
3. Optional: **File › Restart as Administrator** (or the setting to always start that way) lets Temizlikci read every
   folder and the Master File Table.

The installer isn't code-signed yet, so Windows SmartScreen may say *"Windows protected your PC"*. Choose
**More info › Run anyway**. The source and the build script are in this repository.

Temizlikci tells you when a new version is out. It never installs anything by itself: you download the new installer
and run it, and it updates Temizlikci in place. Uninstall it from **Settings › Apps** like any app; your saved scans in
`%LOCALAPPDATA%\Temizlikci` stay until you delete them.

**Requirements:** Windows 11 (22H2 or later), x64.

## Build from source

Requires the .NET 10 SDK on Windows 11 (x64). No Visual Studio needed.

```powershell
git clone https://github.com/akinalpfdn/Temizlikci-Windows.git
cd Temizlikci-Windows
dotnet build Temizlikci.slnx -c Debug
dotnet test --project tests\Temizlikci.Tests
.\src\Temizlikci.App\bin\Debug\net10.0-windows10.0.26100.0\win-x64\Temizlikci.exe
```

`.\scripts\publish.ps1` runs the tests and builds the installer and the portable zip in `artifacts\` (the installer
needs [Inno Setup 6](https://jrsoftware.org/isinfo.php): `winget install JRSoftware.InnoSetup`).

The tests use temporary folders and in-memory stubs. They never touch your real files, your Recycle Bin, or your WSL
distributions.

## How it's built

WinUI 3 (Windows App SDK, unpackaged and self-contained) on .NET 10, with Win2D for the chart and no other third-party
UI code. The scanner reads directories with `NtQueryDirectoryFile` off the UI thread, or the Master File Table directly
when elevated. View models live in a library without WinUI types, so they are unit-tested; every user-facing string
lives in one resource file, and every color comes from the theme, with High Contrast mapped to system colors.

| Path | Contents |
|---|---|
| `src/Temizlikci.Domain` | The scan tree, cleanup rules, history, chart layout — plain .NET with tests |
| `src/Temizlikci.Services` | Scanners, Recycle Bin, volumes, Git, WSL, DISM, editors, update check — Windows implementations |
| `src/Temizlikci.Presentation` | View models, strings, formatting, chart palette |
| `src/Temizlikci.App` | WinUI views, theme resources, the Win2D chart |
| `tests/Temizlikci.Tests` | xUnit v3 suites |
| `installer/` | The Inno Setup script |

Why things are the way they are is written down in [DECISIONS.md](DECISIONS.md).

## License

[MIT](LICENSE) © Akinalp Fidan
