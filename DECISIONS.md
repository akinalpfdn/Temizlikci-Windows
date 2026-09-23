# Decisions

This file tracks all non-trivial technical decisions made during this project.
Format: see `rules/common/decisions.md`. Append-only. The macOS project's `../temizlikci/DECISIONS.md` still
explains every behavior this port inherits; entries here cover what differs on Windows.

---

## 2026-09-23 — C# and WinUI 3 for the Windows port
**Chosen:** User decision: C# on .NET 10 with WinUI 3 (Windows App SDK 2.5), Win2D for the chart.
**Alternatives:** Rust + Tauri (web UI); Swift on Windows reusing the domain code; WPF.
**Why:** WinUI 3 is Windows' own UI stack (Fluent, Mica, system theming), the Windows counterpart of "native
SwiftUI, HIG-aligned". C# makes Win32 interop (directory enumeration, raw volume reads, shell COM) straightforward.
Swift on Windows has no mature UI layer (swift-winrt), so shared domain code would cost more in toolchain problems than
it saves. Tauri gives a fast scanner but a web UI. WPF lacks Mica and current Fluent controls. Verified before
planning: an unpackaged WinUI 3 + Win2D app builds with the `dotnet` CLI alone and runs on this machine.
**Trade-offs:** Nothing is shared with the macOS code; behavior is kept in sync by porting tests with the code.
**Revisit if:** Swift on Windows gains a usable UI layer.

---

## 2026-09-23 — Unpackaged, self-contained Windows App SDK
**Chosen:** `WindowsPackageType=None`, `WindowsAppSDKSelfContained=true`, x64 only.
**Alternatives:** MSIX packaging; framework-dependent Windows App SDK runtime.
**Why:** The app is for the developer's own machine first. MSIX adds signing and install ceremony and a container that
virtualizes `AppData` writes. Self-contained means no runtime installer; the output folder runs as is.
**Trade-offs:** Larger output folder; no Store updates; settings live in a JSON file instead of `ApplicationData`.
**Revisit if:** The app is distributed to others through the Store or winget.

---

## 2026-09-23 — Four projects with dependencies pointing inward
**Chosen:** `Temizlikci.Domain` (pure .NET, no Windows APIs), `Temizlikci.Services` (Windows implementations),
`Temizlikci.Presentation` (view models, strings, formatting — no WinUI types), `Temizlikci.App` (WinUI views and
composition root), plus `Temizlikci.Tests`.
**Alternatives:** One app project with folders, like the macOS target.
**Why:** WinUI projects can't be referenced by an ordinary test project, so anything inside the app is untestable.
Keeping view models free of WinUI types lets the macOS view-model tests (navigation, trash, cache refresh) be ported.
The separate Domain project enforces that rules, history and layout never touch Win32.
**Trade-offs:** More projects; view models can't use WinUI types (colors are roles, resolved by the views).
**Revisit if:** A second front end (CLI) appears — the split already allows it.

---

## 2026-09-23 — Elevation replaces Full Disk Access
**Chosen:** The app starts unelevated (`asInvoker`). After a scan that couldn't read folders, a banner offers
"Restart as Administrator" (the counterpart of the macOS Full Disk Access banner). A setting makes the app restart
itself elevated at launch. Elevated, the scanner enables SeBackupPrivilege and uses the MFT reader on NTFS volumes.
**Alternatives:** `requireAdministrator` in the manifest (always elevated); never elevate.
**Why:** The developer is fine with granting admin, and admin is what makes whole-disk scans complete and fast. But an
always-elevated deletion tool turns every mistake into a system-wide one, and elevated windows can't accept drag and
drop from Explorer. Asking in context, once, mirrors the macOS flow; the setting removes the prompt fatigue for the
developer.
**Trade-offs:** One UAC prompt per elevated launch. Unelevated scans of `C:\` leave some system folders unread.
**Revisit if:** Unelevated scanning turns out to be useless in practice for the developer.

---

## 2026-09-23 — The app protects system locations itself
**Chosen:** Move to Recycle Bin is refused for drive roots, `C:\Windows`, `C:\Program Files`, `C:\Program Files (x86)`,
`C:\ProgramData`, `C:\Users` and each profile root, `$Recycle.Bin`, `System Volume Information`, `pagefile.sys`,
`hiberfil.sys`, `swapfile.sys`, and everything inside the first seven — in addition to the Keep / Remove with Tool rules.
**Alternatives:** Rely on the cleanup rules alone, as on macOS.
**Why:** macOS protects `/System` with SIP and the macOS app never runs as root. On Windows an elevated process can
recycle `C:\Windows\System32`. A hard floor in the app is the only thing between a misclick and an unbootable machine.
**Trade-offs:** Legitimate deletions inside `ProgramData` (e.g. an uninstalled app's leftovers) must be done in Explorer.
**Revisit if:** The developer needs specific ProgramData leftovers removed often — add rules for them instead.

---

## 2026-09-23 — Directory scanner reads NtQueryDirectoryFile, not System.IO
**Chosen:** Each folder is listed with `NtQueryDirectoryFile(FileIdFullDirectoryInformation)` on a handle opened with
`FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT`. The returned entries carry allocation size, file ID,
attributes, reparse tag and timestamps, so no file is ever opened. Hard links are counted once by file ID.
**Alternatives:** `System.IO.Enumeration` / `FileSystemInfo` (no allocation size, no file ID); `FindFirstFileEx` (no
allocation size); opening each file for `GetFileInformationByHandleEx` (one open per file — far too slow).
**Why:** Allocated size (not length) and hard-link identity are correctness rules inherited from macOS, and this is the
only enumeration API that returns both in bulk.
**Trade-offs:** Native interop in the hot path; the file-ID set costs ~20 bytes per file during a scan.
**Revisit if:** .NET exposes allocation size and file ID on `FileSystemEntry`.

---

## 2026-09-23 — Folder explanations are deterministic only
**Chosen:** "What Is This?" identifies known Windows folders, installed apps (uninstall registry, AppX packages) and
file types (shell type names). There is no generated explanation.
**Alternatives:** Windows AI APIs (Phi Silica); a cloud model.
**Why:** Windows' on-device model APIs need a Copilot+ PC and a packaged app with a limited-access feature; this app is
unpackaged and the machine isn't one. A cloud model would send folder paths off the machine, which the macOS app
explicitly refuses to do.
**Trade-offs:** Unrecognized folders get no explanation.
**Revisit if:** Windows offers a general on-device model API for desktop apps.

---

## 2026-09-23 — Sizes in binary units, like Explorer
**Chosen:** Sizes are formatted with 1024-based units and Explorer's labels (KB, MB, GB), three significant digits.
**Alternatives:** Decimal units like the macOS app (`ByteCountFormatStyle.file`).
**Why:** Windows shows binary sizes everywhere (Explorer, Settings › Storage, disk properties). The same folder showing
a different number in Temizlikci and in Explorer's Properties would read as a bug.
**Trade-offs:** Numbers differ from the macOS app for the same bytes.
**Revisit if:** Windows switches to decimal units.

---

## 2026-09-23 — Commits straight to main, tagged per phase
**Chosen:** Solo project in early development: commit each phase to `main`, annotated tag `v0.N.0` per phase, push to
the private GitHub repository `akinalpfdn/Temizlikci-Windows`.
**Alternatives:** Feature branch + squash merge per phase.
**Why:** Git rules allow direct commits while prototyping without users; the phase tags give the same rollback points.
**Trade-offs:** No PR review trail.
**Revisit if:** The app gets users or contributors — switch to squash-merged branches.

---

## 2026-09-23 — Tree nodes store names, not paths
**Chosen:** `FileNode` holds its name only (the root's name is the scanned path). Paths and IDs are derived while
walking (`NodeRef` = node + path); an ID is the full path, or the folder's path plus a suffix for aggregates, exactly
as on macOS. Nodes stay immutable; edits rebuild the folders on the way (copy-on-write).
**Alternatives:** A path per node like macOS (`URL`); a mutable tree with parent pointers.
**Why:** The macOS app measured a second copy of every path as its largest memory cost (Lore knownIssue 101). Parent
pointers would make shared subtrees impossible, so every trash edit would have to mutate a tree that background
analyses are still reading.
**Trade-offs:** Code that needs a path must carry it (`NodeRef`, `IdPath`); a node alone doesn't know where it is.
**Revisit if:** Profiling shows path rebuilding in hot UI paths.

---

## 2026-09-23 — Rules are data in the domain; their wording lives in the presentation layer
**Chosen:** `CleanupRule` carries an ID, ecosystem, safety, matcher and action; the reason people read is looked up by
rule ID in `L10n`. Matchers gained `AtDriveRoot` (for `$Recycle.Bin`, `hiberfil.sys` on any drive) and glob markers
(`*.csproj`, `*.sln`), and exact-path rules also match files.
**Alternatives:** Resource keys stored in the domain, as `LocalizedStringResource` was on macOS.
**Why:** The domain project has no resources and must stay free of presentation concerns. On Windows several of the
largest space users are single files at a drive root (hibernation and page files), and .NET projects are recognized by
`*.csproj` whose name varies.
**Trade-offs:** A new rule needs a matching reason string; a test enforces it.
**Revisit if:** Rules become user-editable.

---

## 2026-09-23 — WSL: registry inventory, diskpart compaction, Docker's distributions left alone
**Chosen:** Distributions are read from `HKCU\...\Lxss` (name, folder, version, default); whether one runs comes from
`wsl --list --running --quiet`. Compact Disk stops the distribution with `wsl --terminate` and runs a `diskpart /s`
script (attach read-only, compact, detach), which needs administrator rights. Remove uses `wsl --unregister` after a
confirmation. Distributions named `docker-desktop*` get no actions.
**Alternatives:** Parsing `wsl --list --verbose`; `Optimize-VHD`; `wsl --manage --set-sparse`; `wsl --shutdown`.
**Why:** `--list --verbose` prints a localized table (Turkish on this PC), so parsing it breaks by language; the registry
and the quiet list are locale-free. `Optimize-VHD` needs the Hyper-V PowerShell module, which Home editions lack.
Sparse mode only returns space freed after it is switched on. `--shutdown` would stop Docker too. diskpart reports
failure through its exit code only in `/s` mode, hence a script file (rewritten in the app folder) rather than stdin.
Stopping or removing Docker's distributions breaks Docker Desktop, which manages that data itself.
**Trade-offs:** Compacting needs the app elevated; the shield on the button says so and the error offers a restart.
**Revisit if:** WSL gains a supported compact command.
