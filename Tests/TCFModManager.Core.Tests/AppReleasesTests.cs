using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class AppReleasesTests
{
    private static ModVersion V(string? version, string published, string? notes = "<p>x</p>") =>
        new() { Version = version, PublishedAt = DateTimeOffset.Parse(published), Description = notes };

    [Fact]
    public void NewestVersionNumberFirst_NotPublishDate()
    {
        var releases = AppUpdateService.ToReleases(
        [
            V("1.9.0-beta", "2026-09-02"),
            V("1.10.0-beta", "2026-09-03"),
            V("1.8.1-beta", "2026-09-05"),
        ]);

        Assert.Equal(["1.10.0-beta", "1.9.0-beta", "1.8.1-beta"], releases.Select(r => r.Version));
    }

    [Fact]
    public void UnparsableAfterParsable_BlankAndDuplicatesDropped()
    {
        var releases = AppUpdateService.ToReleases(
        [
            V("odd", "2026-11-01"),
            V("1.2.0-beta", "2026-08-01"),
            V("", "2026-08-02"),
            V("1.2.0-BETA", "2026-08-03"),
        ]);

        Assert.Equal(["1.2.0-beta", "odd"], releases.Select(r => r.Version));
    }

    [Fact]
    public void BlankNotesBecomeNull()
    {
        var release = Assert.Single(AppUpdateService.ToReleases([V("1.0.0", "2026-08-01", "  ")]));

        Assert.Null(release.Notes);
    }
}
