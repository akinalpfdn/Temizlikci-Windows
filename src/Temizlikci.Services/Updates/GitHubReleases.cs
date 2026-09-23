using System.Net;
using System.Net.Http.Headers;
using Temizlikci.Domain.Updates;

namespace Temizlikci.Services.Updates;

/// <summary>Asks GitHub for the Windows app's latest release. Reads one small JSON document; downloads nothing else.</summary>
public sealed class GitHubReleases : IUpdateChecker, IDisposable
{
    private static readonly Uri Latest = new("https://api.github.com/repos/akinalpfdn/Temizlikci-Windows/releases/latest");

    private readonly HttpClient client;

    public GitHubReleases(string userAgentVersion)
    {
        client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Temizlikci-Windows", userAgentVersion));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    /// <exception cref="HttpRequestException">GitHub couldn't be reached or answered with an error.</exception>
    public async Task<Release?> LatestReleaseAsync(CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(Latest, cancellationToken).ConfigureAwait(false);
        // No release yet, or the repository isn't public: there is simply nothing newer to offer.
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return Release.FromGitHub(json);
    }

    public void Dispose() => client.Dispose();
}
