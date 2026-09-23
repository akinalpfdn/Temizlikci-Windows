# Temizlikci for Windows

**See what's filling your PC. Clean up what's safe. Keep what isn't.**
A native Windows 11 disk analyzer that knows what developer files are and which ones you can delete — the Windows
port of [Temizlikci for macOS](https://github.com/akinalpfdn/Temizlikci).

> Work in progress. See `DEVPLAN.md` for the phases.

## Build from source

Requires the .NET 10 SDK on Windows 11 (x64). No Visual Studio needed.

```powershell
dotnet build Temizlikci.slnx -c Debug
dotnet test --project tests\Temizlikci.Tests
.\src\Temizlikci.App\bin\Debug\net10.0-windows10.0.26100.0\win-x64\Temizlikci.exe
```

The tests use temporary folders and in-memory stubs. They never touch your real files, your Recycle Bin, or your WSL
distributions.

## How it's built

WinUI 3 (Windows App SDK, unpackaged and self-contained) on .NET 10, with Win2D for the chart. View models live in a
library without WinUI types so they are unit-tested; every user-facing string lives in one resource file, and every
color comes from the theme.

| Path | Contents |
|---|---|
| `src/Temizlikci.Domain` | The scan tree, cleanup rules, history, chart layout — plain .NET with tests |
| `src/Temizlikci.Services` | Scanners, Recycle Bin, volumes, Git, WSL, update check — Windows implementations |
| `src/Temizlikci.Presentation` | View models, strings, formatting, chart palette |
| `src/Temizlikci.App` | WinUI views, theme resources, the Win2D chart |
| `tests/Temizlikci.Tests` | xUnit v3 suites |

Why things are the way they are is written down in [DECISIONS.md](DECISIONS.md).

## License

[MIT](LICENSE) © Akinalp Fidan
