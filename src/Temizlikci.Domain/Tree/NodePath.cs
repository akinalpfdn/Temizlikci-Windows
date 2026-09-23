namespace Temizlikci.Domain.Tree;

/// <summary>
/// Windows path arithmetic for the scan tree. Paths use backslashes, drive roots keep their trailing separator
/// (<c>C:\</c>), everything else has none. Comparisons ignore case, like the file system.
/// </summary>
public static class NodePath
{
    public const char Separator = '\\';

    /// <summary>Compares paths the way NTFS does by default: ordinal, ignoring case.</summary>
    public static StringComparer Comparer { get; } = StringComparer.OrdinalIgnoreCase;

    public static string Join(string parent, string name)
    {
        ArgumentNullException.ThrowIfNull(parent);
        return parent.EndsWith(Separator) ? parent + name : parent + Separator + name;
    }

    /// <summary>Normalizes separators and drops a trailing one, except on a drive root.</summary>
    public static string Trim(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string normalized = path.Replace('/', Separator);
        while (normalized.Length > 3 && normalized.EndsWith(Separator)) normalized = normalized[..^1];
        if (normalized.Length == 2 && normalized[1] == ':') normalized += Separator;
        return normalized;
    }

    public static bool IsDriveRoot(string path) =>
        path is { Length: 3 } && path[1] == ':' && path[2] == Separator && char.IsLetter(path[0]);

    /// <summary>The containing folder, or <c>null</c> for a drive root or a bare name.</summary>
    public static string? Parent(string path)
    {
        string trimmed = Trim(path);
        if (IsDriveRoot(trimmed)) return null;
        int index = trimmed.LastIndexOf(Separator);
        if (index < 0) return null;
        if (index == 2 && trimmed[1] == ':') return trimmed[..3];
        return trimmed[..index];
    }

    /// <summary>The last component; a drive root names itself (<c>C:\</c>).</summary>
    public static string Name(string path)
    {
        string trimmed = Trim(path);
        if (IsDriveRoot(trimmed)) return trimmed;
        int index = trimmed.LastIndexOf(Separator);
        return index < 0 ? trimmed : trimmed[(index + 1)..];
    }

    /// <summary>True when <paramref name="path"/> is strictly inside <paramref name="ancestor"/>.</summary>
    public static bool IsWithin(string path, string ancestor)
    {
        string trimmedAncestor = Trim(ancestor);
        string prefix = trimmedAncestor.EndsWith(Separator) ? trimmedAncestor : trimmedAncestor + Separator;
        string trimmedPath = Trim(path);
        return trimmedPath.Length > prefix.Length && trimmedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSameOrWithin(string path, string ancestor) =>
        Comparer.Equals(Trim(path), Trim(ancestor)) || IsWithin(path, ancestor);

    /// <summary>The file extension without the dot, or empty.</summary>
    public static string Extension(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        int dot = name.LastIndexOf('.');
        return dot <= 0 || dot == name.Length - 1 ? string.Empty : name[(dot + 1)..];
    }
}
