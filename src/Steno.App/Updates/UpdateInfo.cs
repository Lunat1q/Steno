namespace Steno.App.Updates;

/// <param name="Version">The released version, parsed from the tag (e.g. "v1.2.0" → 1.2.0).</param>
/// <param name="Notes">Release body, shown to the user before they agree to install.</param>
/// <param name="DownloadUrl">The .msi asset.</param>
/// <param name="ChecksumUrl">SHA-256 of the .msi, published alongside it. Null if the release has none.</param>
public sealed record UpdateInfo(
    Version Version,
    string Notes,
    Uri DownloadUrl,
    Uri? ChecksumUrl,
    long SizeBytes)
{
    public string SizeText => $"{SizeBytes / 1024d / 1024d:N0} MB";
}

public interface IUpdateChecker
{
    /// <summary>The newest release, or null when this build is already current.</summary>
    Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken = default);
}

public interface IUpdateInstaller
{
    /// <summary>
    /// Downloads the installer, verifies it, and hands it to Windows. The app exits so the MSI
    /// can replace files that are otherwise locked.
    /// </summary>
    Task DownloadAndInstallAsync(
        UpdateInfo update,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}
