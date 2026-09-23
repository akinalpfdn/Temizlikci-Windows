using Temizlikci.Domain.Cleanup;

namespace Temizlikci.Presentation.Developer;

/// <summary>One ecosystem's matches in the Developer view, largest first.</summary>
/// <param name="Reclaimable">What its non-Keep items add up to: the space its actions can give back.</param>
public sealed record DeveloperGroup(Ecosystem Ecosystem, IReadOnlyList<CleanupMatch> Matches, long Reclaimable)
{
    public string Id => Ecosystem.ToString();
}

/// <summary>How the Developer view arranges a scan's cleanup matches.</summary>
public static class DeveloperSummary
{
    /// <summary>Groups in the ecosystems' display order, skipping empty ones.</summary>
    public static IReadOnlyList<DeveloperGroup> Groups(IEnumerable<CleanupMatch> matches)
    {
        ArgumentNullException.ThrowIfNull(matches);
        return matches
            .GroupBy(match => match.Rule.Ecosystem)
            .OrderBy(group => group.Key)
            .Select(group => new DeveloperGroup(
                group.Key,
                group.OrderByDescending(match => match.Node.AllocatedSize).ToList(),
                group.Where(match => match.Rule.Safety != SafetyLevel.Keep).Sum(match => match.Node.AllocatedSize)))
            .ToList();
    }

    /// <summary>Space per safety level, in Safe, Tool, Keep order, zeros included, for the summary bar and its legend.</summary>
    public static IReadOnlyList<(SafetyLevel Level, long Bytes)> Totals(IEnumerable<CleanupMatch> matches)
    {
        ArgumentNullException.ThrowIfNull(matches);
        var list = matches.ToList();
        return Enum.GetValues<SafetyLevel>()
            .Select(level => (level, list.Where(match => match.Rule.Safety == level).Sum(match => match.Node.AllocatedSize)))
            .ToList();
    }

    /// <summary>The group opened first: the one with the most space that is safe to remove, so the screen isn't empty.</summary>
    public static Ecosystem? OpenedFirst(IReadOnlyList<DeveloperGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        var best = groups
            .Select(group => (group.Ecosystem, Safe: group.Matches.Where(match => match.Rule.Safety == SafetyLevel.Safe).Sum(match => match.Node.AllocatedSize)))
            .Where(item => item.Safe > 0)
            .OrderByDescending(item => item.Safe)
            .FirstOrDefault();
        return best.Safe > 0 ? best.Ecosystem : null;
    }
}
