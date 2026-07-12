using Steno.App.ViewModels;
using Xunit;

namespace Steno.App.Tests;

public class AppVersionTests
{
    [Fact]
    public void The_version_is_a_number_a_human_can_read_back_to_you()
    {
        var version = AppVersion.Current;

        Assert.False(string.IsNullOrWhiteSpace(version));

        // The git hash from InformationalVersion ("1.1.0+a97ec97…") is noise on a title bar.
        Assert.DoesNotContain('+', version);

        // It must be a version, not "dev" or some placeholder — the updater compares against it.
        Assert.True(Version.TryParse(version, out _), $"'{version}' is not a version number");
    }

    [Fact]
    public void It_reports_Stenos_version_and_not_the_hosts()
    {
        // Under a test host the *entry* assembly is the runner. Reading that would report the
        // runner's version in the app's title bar, and silently break the update comparison.
        Assert.Equal(
            typeof(AppVersion).Assembly.GetName().Version!.ToString(3),
            AppVersion.Current);
    }
}
