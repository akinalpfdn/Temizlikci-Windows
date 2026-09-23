using Temizlikci.Domain.History;
using Temizlikci.Domain.Projects;

namespace Temizlikci.Presentation.Main;

/// <summary>What the app remembers between launches. Plain values; the store decides where they live.</summary>
public sealed record AppSettings
{
    /// <summary>How old a saved scan may get before the app refreshes it in the background.</summary>
    public RefreshPeriod RefreshPeriod { get; init; } = RefreshPeriod.ThreeDays;

    /// <summary>Restart elevated at launch, so whole-disk scans read everything (one UAC prompt per launch).</summary>
    public bool StartAsAdministrator { get; init; }

    public bool CheckForUpdates { get; init; } = true;

    public DateTime? LastUpdateCheckUtc { get; init; }

    /// <summary>A version whose banner the person dismissed.</summary>
    public string? DismissedVersion { get; init; }

    public StalePeriod StalePeriod { get; init; } = StalePeriod.Quarter;

    public bool HasSeenChartIntro { get; init; }
}

public interface ISettingsStore
{
    AppSettings Load();

    void Save(AppSettings settings);
}

/// <summary>Asks the person for a folder to scan. Implemented by the view layer (the system picker).</summary>
public interface IFolderPicker
{
    Task<string?> PickFolderAsync();
}
