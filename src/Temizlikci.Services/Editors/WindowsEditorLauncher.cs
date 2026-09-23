using System.Diagnostics;
using Microsoft.Win32;
using Temizlikci.Domain.Projects;

namespace Temizlikci.Services.Editors;

/// <summary>
/// Finds Visual Studio, Android Studio and VS Code and starts them. Where they live is worked out once, on the thread
/// pool at startup: vswhere is a process launch, and the registry and folders don't change while the app runs.
/// </summary>
public sealed class WindowsEditorLauncher : IEditorLauncher
{
    private readonly Lazy<IReadOnlyDictionary<Editor, string>> executables = new(Resolve, LazyThreadSafetyMode.ExecutionAndPublication);

    public WindowsEditorLauncher()
    {
        _ = Task.Run(() => executables.Value);
    }

    public bool IsInstalled(Editor editor) => executables.Value.ContainsKey(editor);

    public void Open(EditorTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        Launch(target.Editor, target.Path);
    }

    public void Start(Editor editor) => Launch(editor, null);

    private void Launch(Editor editor, string? path)
    {
        if (!executables.Value.TryGetValue(editor, out var executable)) return;
        var start = new ProcessStartInfo(executable) { UseShellExecute = false };
        if (path is not null) start.ArgumentList.Add(path);
        using var _ = Process.Start(start);
    }

    private static Dictionary<Editor, string> Resolve()
    {
        var found = new Dictionary<Editor, string>();
        if (VisualStudio() is { } visualStudio) found[Editor.VisualStudio] = visualStudio;
        if (AndroidStudio() is { } androidStudio) found[Editor.AndroidStudio] = androidStudio;
        if (VisualStudioCode() is { } code) found[Editor.VisualStudioCode] = code;
        return found;
    }

    /// <summary>vswhere knows every installed edition and picks the newest; App Paths can lag behind an upgrade.</summary>
    private static string? VisualStudio()
    {
        string vswhere = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft Visual Studio", "Installer", "vswhere.exe");
        if (File.Exists(vswhere))
        {
            var start = new ProcessStartInfo(vswhere) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
            foreach (var argument in new[] { "-latest", "-prerelease", "-property", "productPath" }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start);
            if (process is not null)
            {
                string path = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();
                if (File.Exists(path)) return path;
            }
        }
        return AppPath("devenv.exe");
    }

    /// <summary>The installer records its folder; otherwise any Program Files\Android\*\bin\studio64.exe, newest first.</summary>
    private static string? AndroidStudio()
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var key = machine.OpenSubKey(@"SOFTWARE\Android Studio");
            if (key?.GetValue("Path") is string folder && Existing(Path.Join(folder, "bin", "studio64.exe")) is { } recorded) return recorded;
        }
        var candidates = new List<string>();
        string android = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Android");
        if (Directory.Exists(android)) candidates.AddRange(Directory.EnumerateDirectories(android).Select(folder => Path.Join(folder, "bin", "studio64.exe")));
        candidates.Add(Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Android Studio", "bin", "studio64.exe"));
        return candidates.Where(File.Exists).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
    }

    private static string? VisualStudioCode() =>
        AppPath("Code.exe")
        ?? Existing(Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Microsoft VS Code", "Code.exe"))
        ?? Existing(Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft VS Code", "Code.exe"));

    /// <summary>The executable an app registered under App Paths, per user first.</summary>
    private static string? AppPath(string name)
    {
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var key = hive.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{name}");
            if (key?.GetValue(null) is string path && Existing(path.Trim('"')) is { } existing) return existing;
        }
        return null;
    }

    private static string? Existing(string path) => File.Exists(path) ? path : null;
}
