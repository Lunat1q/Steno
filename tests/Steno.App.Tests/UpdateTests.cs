using Microsoft.Extensions.Logging.Abstractions;
using Steno.App.Updates;
using Steno.App.ViewModels;
using Xunit;

namespace Steno.App.Tests;

public class UpdateVersionTests
{
    [Theory]
    [InlineData("v1.2.0", "1.2.0")]
    [InlineData("1.2.0", "1.2.0")]
    [InlineData("V2.0.1", "2.0.1")]
    public void Release_tags_are_parsed(string tag, string expected)
    {
        Assert.True(GitHubUpdateChecker.TryParseVersion(tag, out var version));
        Assert.Equal(Version.Parse(expected), version);
    }

    [Theory]
    [InlineData("nightly")]
    [InlineData("")]
    [InlineData(null)]
    public void Tags_that_are_not_versions_are_rejected(string? tag) =>
        Assert.False(GitHubUpdateChecker.TryParseVersion(tag, out _));
}

/// <summary>
/// The updater downloads a file and then executes it. These tests pin the refusals — the paths
/// that must hold even when everything else is wrong.
/// </summary>
public class UpdateInstallerSafetyTests
{
    private static MsiUpdateInstaller Installer() => new(NullLogger<MsiUpdateInstaller>.Instance);

    private static UpdateInfo Update(string url, string? checksumUrl) => new(
        new Version(9, 9, 9),
        "notes",
        new Uri(url),
        checksumUrl is null ? null : new Uri(checksumUrl),
        1024);

    [Fact]
    public async Task An_update_with_no_checksum_is_refused()
    {
        var update = Update("https://github.com/Lunat1q/Steno/releases/download/v9.9.9/Steno-Setup.msi", null);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Installer().DownloadAndInstallAsync(update));

        Assert.Contains("cannot be verified", error.Message);
    }

    [Theory]
    // Plain HTTP: anyone on the path could swap the installer.
    [InlineData("http://github.com/Lunat1q/Steno/releases/download/v9/Steno-Setup.msi")]
    // A host we never publish to.
    [InlineData("https://evil.example.com/Steno-Setup.msi")]
    // Lookalike host — the check is an exact host match, not a suffix match.
    [InlineData("https://github.com.evil.example.com/Steno-Setup.msi")]
    public async Task Untrusted_download_urls_are_refused_before_anything_is_downloaded(string url)
    {
        var update = Update(url, "https://github.com/x.sha256");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Installer().DownloadAndInstallAsync(update));

        Assert.Contains("Refusing to download", error.Message);
    }
}

public class UpdateOfferTests
{
    private sealed class NoUpdate : IUpdateChecker
    {
        public Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<UpdateInfo?>(null);
    }

    private sealed class Offered : IUpdateChecker
    {
        public Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<UpdateInfo?>(new UpdateInfo(
                new Version(2, 0, 0),
                "notes",
                new Uri("https://github.com/Lunat1q/Steno/releases/download/v2.0.0/Steno-Setup.msi"),
                new Uri("https://github.com/Lunat1q/Steno/releases/download/v2.0.0/Steno-Setup.msi.sha256"),
                55_000_000));
    }

    private sealed class NeverInstalls : IUpdateInstaller
    {
        public Task DownloadAndInstallAsync(
            UpdateInfo update,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("should not have been called");
    }

    private static UpdateViewModel Build(IUpdateChecker checker) =>
        new(checker, new NeverInstalls(), NullLogger<UpdateViewModel>.Instance);

    [Fact]
    public async Task Nothing_is_offered_when_the_build_is_current()
    {
        var update = Build(new NoUpdate());
        await update.CheckInBackgroundAsync();

        Assert.False(update.IsOffered);
    }

    [Fact]
    public async Task A_newer_release_is_offered_with_its_size()
    {
        var update = Build(new Offered());
        await update.CheckInBackgroundAsync();

        Assert.True(update.IsOffered);
        Assert.Contains("2.0.0", update.Headline);
        Assert.Contains("52 MB", update.Headline);
    }

    [Fact]
    public async Task Dismissing_the_offer_makes_it_go_away()
    {
        var update = Build(new Offered());
        await update.CheckInBackgroundAsync();

        update.DismissCommand.Execute(null);

        Assert.False(update.IsOffered);
    }
}
