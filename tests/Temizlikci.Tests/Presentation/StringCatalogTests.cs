using System.Text.RegularExpressions;
using System.Xml.Linq;
using Temizlikci.Tests.Support;

namespace Temizlikci.Tests.Presentation;

/// <summary>Keeps <c>L10n.cs</c> and <c>Strings.resx</c> in step, like the macOS StringCatalogTests.</summary>
public sealed partial class StringCatalogTests
{
    private static readonly string CodePath = Path.Combine(SourceTree.Source, "Temizlikci.Presentation", "Strings", "L10n.cs");
    private static readonly string ResxPath = Path.Combine(SourceTree.Source, "Temizlikci.Presentation", "Strings", "Strings.resx");

    [GeneratedRegex("""(?:Get|Format)\("([^"]+)"[,)]""")]
    private static partial Regex KeyUse();

    private static HashSet<string> CodeKeys()
    {
        var keys = KeyUse().Matches(File.ReadAllText(CodePath)).Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        keys.UnionWith(ComputedKeys());
        return keys;
    }

    /// <summary>Keys the code builds from rule IDs, enum names and the example folders; each must exist.</summary>
    private static IEnumerable<string> ComputedKeys()
    {
        foreach (var rule in Temizlikci.Domain.Cleanup.CleanupCatalog.Rules) yield return "cleanup.reason." + rule.Id;
        foreach (var ecosystem in Enum.GetValues<Temizlikci.Domain.Cleanup.Ecosystem>()) yield return "cleanup.ecosystem." + ecosystem.ToString().ToLowerInvariant();
        foreach (var folder in Enum.GetValues<Temizlikci.Domain.Identity.KnownFolder>()) yield return "identity." + LowerFirst(folder.ToString());
        foreach (var evidence in Enum.GetValues<Temizlikci.Domain.Projects.ProjectEvidence>()) yield return "projects.evidence." + LowerFirst(evidence.ToString());
        foreach (var folder in Temizlikci.Presentation.Intro.SampleLocation.FolderKeys) yield return "intro.sample." + folder;
    }

    private static string LowerFirst(string name) => char.ToLowerInvariant(name[0]) + name[1..];

    private static Dictionary<string, XElement> ResxEntries() =>
        XDocument.Load(ResxPath).Root!.Elements("data").ToDictionary(data => (string)data.Attribute("name")!, StringComparer.Ordinal);

    [Fact]
    public void Should_HaveAResxEntry_When_CodeUsesAKey()
    {
        var missing = CodeKeys().Except(ResxEntries().Keys).Order().ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public void Should_UseEveryResxEntry_When_TheCatalogIsComplete()
    {
        var orphans = ResxEntries().Keys.Except(CodeKeys()).Order().ToList();
        Assert.Empty(orphans);
    }

    [Fact]
    public void Should_ExplainEveryString_When_ATranslatorReadsTheCatalog()
    {
        var uncommented = ResxEntries().Where(entry => string.IsNullOrWhiteSpace((string?)entry.Value.Element("comment")))
            .Select(entry => entry.Key).Order().ToList();
        Assert.Empty(uncommented);
    }

    [Fact]
    public void Should_KeepPlaceholdersNumbered_When_AValueHasArguments()
    {
        var broken = ResxEntries()
            .Where(entry => (string?)entry.Value.Element("value") is { } value && HasUnbalancedBraces(value))
            .Select(entry => entry.Key).ToList();
        Assert.Empty(broken);
    }

    [Fact]
    public void Should_ResolveEveryKey_When_TheAssemblyIsBuilt()
    {
        var get = typeof(Temizlikci.Presentation.Strings.L10n).GetMethod("Get", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var unresolved = CodeKeys().Where(key => ((string)get.Invoke(null, [key])!).StartsWith('⟦')).ToList();
        Assert.Empty(unresolved);
    }

    private static bool HasUnbalancedBraces(string value)
    {
        int depth = 0;
        foreach (char character in value)
        {
            if (character == '{') depth++;
            if (character == '}') depth--;
            if (depth is < 0 or > 1) return true;
        }
        return depth != 0;
    }
}
