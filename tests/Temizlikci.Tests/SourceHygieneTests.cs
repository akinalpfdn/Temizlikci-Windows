using System.Text.RegularExpressions;
using Temizlikci.Tests.Support;

namespace Temizlikci.Tests;

/// <summary>Rules about the source that a reviewer shouldn't have to catch by eye.</summary>
public sealed partial class SourceHygieneTests
{
    /// <summary>
    /// The only files allowed to delete from disk: the app's own cache and history stores. Everything the person owns
    /// leaves through the Recycle Bin or its owning tool (CLAUDE.md).
    /// </summary>
    private static readonly string[] DeletionAllowed =
    [
        "src/Temizlikci.Services/Persistence/FileScanCache.cs",
        "src/Temizlikci.Services/Persistence/FileSnapshotStore.cs",
        "src/Temizlikci.Services/RecycleBin/ShellRecycleBin.cs",
    ];

    [GeneratedRegex(@"\b(File\.Delete|Directory\.Delete|FileSystemInfo\.Delete|\.Delete\(\s*(true|recursive))")]
    private static partial Regex DeletionCall();

    [GeneratedRegex("#[0-9A-Fa-f]{6,8}\\b")]
    private static partial Regex HexColor();

    [GeneratedRegex("""\b(Text|Title|Content|Header|PlaceholderText|ToolTipService\.ToolTip|AutomationProperties\.Name|Label)="(?!\{)([^"]*[A-Za-z][^"]*)" """)]
    private static partial Regex LiteralText();

    [Fact]
    public void Should_NotDeleteFiles_When_OutsideTheAppsOwnStores()
    {
        var offenders = SourceTree.Files("*.cs")
            .Where(path => !DeletionAllowed.Contains(SourceTree.Relative(path)))
            .Where(path => DeletionCall().IsMatch(File.ReadAllText(path)))
            .Select(SourceTree.Relative)
            .ToList();
        Assert.Empty(offenders);
    }

    [Fact]
    public void Should_TakeColorsFromTheTheme_When_AViewNeedsOne()
    {
        var offenders = Views().Where(path => HexColor().IsMatch(File.ReadAllText(path))).Select(SourceTree.Relative).ToList();
        Assert.Empty(offenders);
    }

    [Fact]
    public void Should_TakeTextFromL10n_When_AViewShowsWords()
    {
        var offenders = Views()
            .SelectMany(path => LiteralText().Matches(File.ReadAllText(path)).Select(match => $"{SourceTree.Relative(path)}: {match.Value}"))
            .ToList();
        Assert.Empty(offenders);
    }

    /// <summary>XAML views: everything except the theme resource dictionaries.</summary>
    private static IEnumerable<string> Views() =>
        SourceTree.Files("*.xaml").Where(path => !SourceTree.Relative(path).Contains("/Theme/", StringComparison.Ordinal));
}
