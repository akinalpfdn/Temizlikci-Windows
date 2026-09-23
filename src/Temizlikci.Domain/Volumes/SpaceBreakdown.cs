namespace Temizlikci.Domain.Volumes;

public enum SpacePartKind
{
    /// <summary>NTFS's own files: the Master File Table, the log, the change journal. Never listed in folders.</summary>
    FileSystemMetadata,
    /// <summary>Restore points and shadow copies (the Volume Shadow Copy storage area).</summary>
    ShadowCopies,
    /// <summary>Used space still unaccounted for after the named parts.</summary>
    Remainder,
}

/// <summary>One named part of the space a whole-volume scan could not attribute to a folder.</summary>
public sealed record SpacePart(SpacePartKind Kind, long Size)
{
    public string Id => Kind.ToString();
}

/// <summary>What the system reports about a volume's hidden space. Parts that can't be read are <c>null</c>.</summary>
public sealed record HiddenSpace(long? FileSystemMetadata, long? ShadowCopies);

/// <summary>Reads the hidden parts of a volume's used space. Most of it needs administrator rights.</summary>
public interface IHiddenSpaceReader
{
    Task<HiddenSpace> ReadAsync(string volumeRoot, CancellationToken cancellationToken);
}

/// <summary>
/// What makes up the "Other Used Space" segment. Parts always add up to <see cref="Total"/>: whatever the named parts
/// don't explain stays visible as the remainder.
/// </summary>
/// <param name="HasUnreadableFolders">True when some folders couldn't be read, so they are counted in the remainder.</param>
public sealed record SpaceBreakdown(long Total, IReadOnlyList<SpacePart> Parts, bool HasUnreadableFolders)
{
    public static SpaceBreakdown Make(long unattributed, HiddenSpace hidden, bool hasUnreadableFolders)
    {
        ArgumentNullException.ThrowIfNull(hidden);
        var parts = new List<SpacePart>();
        if (hidden.FileSystemMetadata is > 0) parts.Add(new SpacePart(SpacePartKind.FileSystemMetadata, hidden.FileSystemMetadata.Value));
        if (hidden.ShadowCopies is > 0) parts.Add(new SpacePart(SpacePartKind.ShadowCopies, hidden.ShadowCopies.Value));
        parts.Sort((left, right) => right.Size.CompareTo(left.Size));

        // The figures come from different sources, so the named parts can overshoot. Scaling them would invent numbers;
        // instead the total grows to hold them, and what is left over stays honest as the remainder.
        long named = parts.Sum(part => part.Size);
        long total = Math.Max(unattributed, named);
        long remainder = total - named;
        if (remainder > 0) parts.Add(new SpacePart(SpacePartKind.Remainder, remainder));
        return new SpaceBreakdown(total, parts, hasUnreadableFolders);
    }
}
