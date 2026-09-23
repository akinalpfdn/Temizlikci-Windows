using CommunityToolkit.Mvvm.ComponentModel;
using Temizlikci.Domain.Actions;
using Temizlikci.Domain.Updates;
using Temizlikci.Presentation.Main;

namespace Temizlikci.Presentation.Updates;

/// <summary>What a check the person asked for found; automatic checks stay silent.</summary>
public enum ManualCheckOutcome
{
    UpToDate,
    Newer,
    Failed,
}

/// <summary>
/// Tells the person when a newer version is published. It never downloads or installs anything by itself: Download
/// opens the zip in the browser (developer decision on macOS, 2026-09-23, kept on Windows).
/// </summary>
public sealed partial class UpdateModel : ObservableObject
{
    /// <summary>Automatic checks happen at most this often.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    private readonly IUpdateChecker checker;
    private readonly Func<AppSettings> loadSettings;
    private readonly Action<AppSettings> saveSettings;
    private readonly IShell shell;

    /// <summary>A newer release the person hasn't dismissed; the banner shows while this is set.</summary>
    [ObservableProperty]
    private Release? available;

    [ObservableProperty]
    private bool isChecking;

    /// <summary>The outcome of Check for Updates…, shown as a dialog, then cleared.</summary>
    [ObservableProperty]
    private ManualCheckOutcome? manualResult;

    public UpdateModel(AppVersion current, IUpdateChecker checker, Func<AppSettings> loadSettings, Action<AppSettings> saveSettings, IShell shell)
    {
        Current = current ?? throw new ArgumentNullException(nameof(current));
        this.checker = checker ?? throw new ArgumentNullException(nameof(checker));
        this.loadSettings = loadSettings ?? throw new ArgumentNullException(nameof(loadSettings));
        this.saveSettings = saveSettings ?? throw new ArgumentNullException(nameof(saveSettings));
        this.shell = shell ?? throw new ArgumentNullException(nameof(shell));
    }

    public AppVersion Current { get; }

    /// <summary>At launch: checks when automatic checks are on and the last one was a day or more ago.</summary>
    public async Task CheckIfDueAsync(DateTime nowUtc)
    {
        var settings = loadSettings();
        if (!settings.CheckForUpdates) return;
        if (settings.LastUpdateCheckUtc is { } last && nowUtc - last < Interval) return;
        await CheckAsync(nowUtc, manual: false).ConfigureAwait(true);
    }

    /// <summary>From the Help menu: always checks, and always reports the outcome.</summary>
    public Task CheckNowAsync(DateTime nowUtc) => CheckAsync(nowUtc, manual: true);

    public void Download()
    {
        if (Available is { } release) shell.OpenUri(release.DownloadUrl);
    }

    public void OpenReleaseNotes()
    {
        if (Available is { } release) shell.OpenUri(release.PageUrl);
    }

    /// <summary>Hides the banner for this version; a later version shows it again.</summary>
    public void Dismiss()
    {
        if (Available is { } release) saveSettings(loadSettings() with { DismissedVersion = release.Version.ToString() });
        Available = null;
    }

    public void ClearManualResult() => ManualResult = null;

    private async Task CheckAsync(DateTime nowUtc, bool manual)
    {
        if (IsChecking) return;
        IsChecking = true;
        try
        {
            var release = await checker.LatestReleaseAsync(CancellationToken.None).ConfigureAwait(true);
            saveSettings(loadSettings() with { LastUpdateCheckUtc = nowUtc });
            if (release is null || release.Version <= Current)
            {
                if (manual) ManualResult = ManualCheckOutcome.UpToDate;
                return;
            }
            if (manual)
            {
                Available = release;
                ManualResult = ManualCheckOutcome.Newer;
            }
            else if (loadSettings().DismissedVersion != release.Version.ToString())
            {
                Available = release;
            }
        }
        catch (HttpRequestException)
        {
            // Offline or GitHub unreachable: an automatic check says nothing and tries again next launch.
            if (manual) ManualResult = ManualCheckOutcome.Failed;
        }
        catch (TaskCanceledException)
        {
            // The request timed out: the same as unreachable.
            if (manual) ManualResult = ManualCheckOutcome.Failed;
        }
        finally
        {
            IsChecking = false;
        }
    }
}
