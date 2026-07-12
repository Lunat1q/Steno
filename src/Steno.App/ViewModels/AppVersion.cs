using System.Reflection;

namespace Steno.App.ViewModels;

/// <summary>
/// The version this copy actually is.
///
/// Shown in the window because it is the only way to answer two questions that come up
/// constantly: "did the update actually install?" and "which version is this bug in?". Without it
/// the updater is unfalsifiable — a banner disappears and you have to take its word.
/// </summary>
public static class AppVersion
{
    /// <summary>e.g. "1.1.0". The build stamps this; a dev build reports whatever the csproj says.</summary>
    public static string Current { get; } = Read();

    private static string Read()
    {
        // This assembly, not the entry assembly: under a test host — or anything else that loads
        // Steno.App — the entry assembly is the host, and we would report *its* version.
        var assembly = typeof(AppVersion).Assembly;

        // InformationalVersion carries the git hash too ("1.1.0+a97ec97…"). Drop it: the number is
        // for humans, and the hash is noise to everyone who is not bisecting.
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
            return informational.Split('+')[0];

        var version = assembly.GetName().Version;
        return version is null ? "dev" : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
