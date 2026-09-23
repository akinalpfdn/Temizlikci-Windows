namespace Temizlikci.Tests.Support;

/// <summary>Locates the repository's source files, for tests that check the code itself.</summary>
internal static class SourceTree
{
    public static string Root { get; } = FindRoot();

    public static string Source => Path.Combine(Root, "src");

    public static IEnumerable<string> Files(string pattern) =>
        Directory.EnumerateFiles(Source, pattern, SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    public static string Relative(string path) => Path.GetRelativePath(Root, path).Replace('\\', '/');

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Temizlikci.slnx")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new InvalidOperationException("Temizlikci.slnx not found above the test output.");
    }
}
