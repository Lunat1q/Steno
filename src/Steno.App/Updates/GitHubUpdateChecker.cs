using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Steno.App.Updates;

/// <summary>
/// Asks GitHub Releases what the newest version is.
///
/// The releases API is the source of truth — publishing a separate feed (a gh-pages JSON file,
/// say) would mean two places to keep in step, and the day they disagree is the day the updater
/// installs the wrong thing.
/// </summary>
public sealed class GitHubUpdateChecker : IUpdateChecker
{
    private const string LatestRelease = "https://api.github.com/repos/Lunat1q/Steno/releases/latest";

    private readonly HttpClient _http;
    private readonly ILogger<GitHubUpdateChecker> _logger;
    private readonly Version _current;

    public GitHubUpdateChecker(ILogger<GitHubUpdateChecker> logger, Version? currentVersion = null)
    {
        _logger = logger;
        _current = currentVersion
                   ?? Assembly.GetEntryAssembly()?.GetName().Version
                   ?? new Version(0, 0, 0);

        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Steno-Updater");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public async Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var release = await _http
                .GetFromJsonAsync<GitHubRelease>(LatestRelease, cancellationToken)
                .ConfigureAwait(false);

            if (release is null || release.Draft || release.Prerelease)
                return null;

            if (!TryParseVersion(release.TagName, out var version) || version <= _current)
                return null;

            var installer = release.Assets.FirstOrDefault(
                a => a.Name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase));

            if (installer is null)
            {
                _logger.LogWarning("Release {Tag} has no .msi asset; ignoring it", release.TagName);
                return null;
            }

            var checksum = release.Assets.FirstOrDefault(
                a => a.Name.Equals(installer.Name + ".sha256", StringComparison.OrdinalIgnoreCase));

            _logger.LogInformation("Update available: {Version} (running {Current})", version, _current);

            return new UpdateInfo(
                version,
                release.Body ?? string.Empty,
                new Uri(installer.DownloadUrl),
                checksum is null ? null : new Uri(checksum.DownloadUrl),
                installer.Size);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or NotSupportedException)
        {
            // Being offline is not an error worth showing anyone. Check again next launch.
            _logger.LogDebug(ex, "Update check failed");
            return null;
        }
    }

    /// <summary>Accepts "1.2.0" and "v1.2.0"; anything else is not a release we understand.</summary>
    internal static bool TryParseVersion(string? tag, out Version version)
    {
        version = new Version(0, 0, 0);

        if (string.IsNullOrWhiteSpace(tag))
            return false;

        var trimmed = tag.TrimStart('v', 'V');
        return Version.TryParse(trimmed, out version!);
    }

    private sealed record GitHubRelease
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; init; }
        [JsonPropertyName("body")] public string? Body { get; init; }
        [JsonPropertyName("draft")] public bool Draft { get; init; }
        [JsonPropertyName("prerelease")] public bool Prerelease { get; init; }
        [JsonPropertyName("assets")] public IReadOnlyList<GitHubAsset> Assets { get; init; } = [];
    }

    private sealed record GitHubAsset
    {
        [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
        [JsonPropertyName("browser_download_url")] public string DownloadUrl { get; init; } = string.Empty;
        [JsonPropertyName("size")] public long Size { get; init; }
    }
}
