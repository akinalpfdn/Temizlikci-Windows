using Temizlikci.Domain.Tree;

namespace Temizlikci.Domain.Cleanup;

/// <summary>
/// The folders rules and protections are written against. Passed in rather than read from the environment where
/// they're used, so tests can describe any machine.
/// </summary>
public sealed record KnownLocations(
    string Home,
    string LocalAppData,
    string RoamingAppData,
    string ProgramData,
    string Windows,
    string ProgramFiles,
    string ProgramFilesX86)
{
    /// <summary>The system drive's root, e.g. <c>C:\</c>.</summary>
    public string SystemDrive => NodePath.Trim(Windows)[..3];

    /// <summary>The folder holding every profile, e.g. <c>C:\Users</c>.</summary>
    public string Users => NodePath.Parent(Home) ?? SystemDrive;

    public static KnownLocations FromEnvironment() => new(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));

    /// <summary>A typical machine for tests: user "dev" on drive C.</summary>
    public static KnownLocations Sample { get; } = new(
        @"C:\Users\dev",
        @"C:\Users\dev\AppData\Local",
        @"C:\Users\dev\AppData\Roaming",
        @"C:\ProgramData",
        @"C:\Windows",
        @"C:\Program Files",
        @"C:\Program Files (x86)");

    /// <summary>Expands <c>~</c>, <c>%LOCALAPPDATA%</c>, <c>%APPDATA%</c>, <c>%PROGRAMDATA%</c>, <c>%WINDIR%</c>,
    /// <c>%SYSTEMDRIVE%</c> and <c>%PROGRAMFILES%</c> at the start of a pattern.</summary>
    public string Expand(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        (string Token, string Value)[] tokens =
        [
            ("~", Home),
            ("%USERPROFILE%", Home),
            ("%LOCALAPPDATA%", LocalAppData),
            ("%APPDATA%", RoamingAppData),
            ("%PROGRAMDATA%", ProgramData),
            ("%WINDIR%", Windows),
            ("%SYSTEMDRIVE%", SystemDrive),
            ("%PROGRAMFILES%", ProgramFiles),
            ("%PROGRAMFILES(X86)%", ProgramFilesX86),
        ];
        foreach (var (token, value) in tokens)
        {
            if (!pattern.StartsWith(token, StringComparison.OrdinalIgnoreCase)) continue;
            string rest = pattern[token.Length..].TrimStart('\\', '/');
            return NodePath.Trim(rest.Length == 0 ? value : NodePath.Join(value, rest));
        }
        return NodePath.Trim(pattern);
    }
}
