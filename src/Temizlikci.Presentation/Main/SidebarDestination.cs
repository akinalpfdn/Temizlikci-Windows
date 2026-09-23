namespace Temizlikci.Presentation.Main;

public enum DestinationKind
{
    Drive,
    Home,
    ChosenFolder,
    Developer,
    WhatGrew,
    LargeFiles,
    RecycleBin,
}

/// <summary>
/// Places and views reachable from the sidebar. The sidebar never holds the folder tree; the hierarchy lives in the
/// content area. Drives carry their root path, because a PC can have several.
/// </summary>
public sealed record SidebarDestination(DestinationKind Kind, string? Path = null)
{
    public static SidebarDestination Home { get; } = new(DestinationKind.Home);
    public static SidebarDestination ChosenFolder { get; } = new(DestinationKind.ChosenFolder);
    public static SidebarDestination Developer { get; } = new(DestinationKind.Developer);
    public static SidebarDestination WhatGrew { get; } = new(DestinationKind.WhatGrew);
    public static SidebarDestination LargeFiles { get; } = new(DestinationKind.LargeFiles);
    public static SidebarDestination RecycleBin { get; } = new(DestinationKind.RecycleBin);

    public static SidebarDestination Drive(string rootPath) => new(DestinationKind.Drive, rootPath);

    public bool IsLocation => Kind is DestinationKind.Drive or DestinationKind.Home or DestinationKind.ChosenFolder;

    /// <summary>A Segoe Fluent Icons glyph.</summary>
    public string Glyph => Kind switch
    {
        DestinationKind.Drive => "\uEDA2",
        DestinationKind.Home => "\uE80F",
        DestinationKind.ChosenFolder => "\uE8B7",
        DestinationKind.Developer => "\uEC7A",
        DestinationKind.WhatGrew => "\uE9D2",
        DestinationKind.LargeFiles => "\uE7C3",
        DestinationKind.RecycleBin => "\uE74D",
        _ => "\uE8B7",
    };
}
