using System.Globalization;
using System.Text.Json;

namespace Temizlikci.Domain.Updates;

/// <summary>A version number like 1.4.2. Tags may carry a leading "v"; missing parts count as zero, so "1.4" equals
/// "1.4.0"; anything after a hyphen or plus (pre-release or build labels) is ignored for ordering.</summary>
public sealed class AppVersion : IComparable<AppVersion>, IEquatable<AppVersion>
{
    private readonly int[] parts;

    private AppVersion(int[] parts) => this.parts = parts;

    public static AppVersion? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string trimmed = text.Trim();
        if (trimmed.StartsWith('v') || trimmed.StartsWith('V')) trimmed = trimmed[1..];
        string core = trimmed.Split('-', '+')[0];
        var pieces = core.Split('.');
        var numbers = new int[pieces.Length];
        for (int index = 0; index < pieces.Length; index++)
        {
            if (!int.TryParse(pieces[index], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[index])) return null;
        }
        return new AppVersion(numbers);
    }

    public int CompareTo(AppVersion? other)
    {
        if (other is null) return 1;
        int count = Math.Max(parts.Length, other.parts.Length);
        for (int index = 0; index < count; index++)
        {
            int left = index < parts.Length ? parts[index] : 0;
            int right = index < other.parts.Length ? other.parts[index] : 0;
            if (left != right) return left.CompareTo(right);
        }
        return 0;
    }

    public bool Equals(AppVersion? other) => other is not null && CompareTo(other) == 0;

    public override bool Equals(object? obj) => obj is AppVersion other && Equals(other);

    public override int GetHashCode()
    {
        // Trailing zeros don't change the version, so they must not change the hash.
        int length = parts.Length;
        while (length > 1 && parts[length - 1] == 0) length--;
        var hash = new HashCode();
        for (int index = 0; index < length; index++) hash.Add(parts[index]);
        return hash.ToHashCode();
    }

    public override string ToString() => string.Join('.', parts.Select(part => part.ToString(CultureInfo.InvariantCulture)));

    public static bool operator <(AppVersion left, AppVersion right) => left is null ? right is not null : left.CompareTo(right) < 0;
    public static bool operator >(AppVersion left, AppVersion right) => left is not null && left.CompareTo(right) > 0;
    public static bool operator <=(AppVersion left, AppVersion right) => left is null || left.CompareTo(right) <= 0;
    public static bool operator >=(AppVersion left, AppVersion right) => left is null ? right is null : left.CompareTo(right) >= 0;
    public static bool operator ==(AppVersion? left, AppVersion? right) => left is null ? right is null : left.Equals(right);
    public static bool operator !=(AppVersion? left, AppVersion? right) => !(left == right);
}

/// <summary>A published release: what it is and where to get it.</summary>
/// <param name="DownloadUrl">The attached installer, else the zip, else the release page.</param>
public sealed record Release(AppVersion Version, Uri PageUrl, Uri DownloadUrl)
{
    /// <summary>Reads GitHub's "latest release" response. <c>null</c> for anything that isn't a usable release.</summary>
    public static Release? FromGitHub(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (Bool(root, "draft") || Bool(root, "prerelease")) return null;
            if (!root.TryGetProperty("tag_name", out var tag) || AppVersion.Parse(tag.GetString()) is not { } version) return null;
            if (!root.TryGetProperty("html_url", out var html) || !Uri.TryCreate(html.GetString(), UriKind.Absolute, out var page)) return null;
            Uri? installer = null;
            Uri? zip = null;
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    string name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
                    if (!asset.TryGetProperty("browser_download_url", out var url) || !Uri.TryCreate(url.GetString(), UriKind.Absolute, out var link)) continue;
                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) installer ??= link;
                    else if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) zip ??= link;
                }
            }
            // The installer upgrades in place; the portable zip is for releases that came before it.
            return new Release(version, page, installer ?? zip ?? page);
        }
        catch (JsonException)
        {
            // Anything GitHub returns that isn't JSON (an HTML error page, a proxy) simply means "no release".
            return null;
        }
    }

    private static bool Bool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}

/// <summary>Asks where the newest release is. Nothing is downloaded or installed here.</summary>
public interface IUpdateChecker
{
    /// <summary>The newest published release, or <c>null</c> when there is none (or the repository isn't public).</summary>
    Task<Release?> LatestReleaseAsync(CancellationToken cancellationToken);
}
