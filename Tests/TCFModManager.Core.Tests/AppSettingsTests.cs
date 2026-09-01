using System.Text.Json;
using TCFModManager.Core.Models;
using Xunit;

namespace TCFModManager.Core.Tests;

public class AppSettingsTests
{
    [Fact]
    public void Theme_FollowsWindowsWhenTheFileHasNeverHeardOfIt()
    {
        // A settings file written before the setting existed has no Theme key, and those installs
        // should start matching Windows like a fresh one - a theme feature nobody finds isn't one.
        // The cost is that upgrading on a light Windows changes the app's appearance once.
        var settings = JsonSerializer.Deserialize<AppSettings>("""{ "SptInstallPath": "C:\\SPT" }""");

        Assert.NotNull(settings);
        Assert.Equal(ThemePreference.FollowSystem, settings!.Theme);
    }

    [Fact]
    public void Theme_KeepsAnExplicitChoiceRatherThanFollowingWindows()
    {
        // Anyone who has picked a theme has said what they want, so the default must not reach back
        // over it on the next launch.
        var settings = JsonSerializer.Deserialize<AppSettings>("""{ "Theme": "Dark" }""");

        Assert.Equal(ThemePreference.Dark, settings!.Theme);
    }

    [Fact]
    public void ModFootprintPage_IsOffUntilSomebodyTurnsItOn()
    {
        // Opt-in on purpose. The page describes what a mod ships and times nothing, so it needs its
        // own explanation to be read correctly - an unexplained "Heavy" badge appearing in the
        // sidebar of an install that upgraded into it is exactly what this default prevents.
        var fresh = new AppSettings();
        var upgraded = JsonSerializer.Deserialize<AppSettings>("""{ "SptInstallPath": "C:\\SPT" }""");

        Assert.False(fresh.ShowModFootprintPage);
        Assert.False(upgraded!.ShowModFootprintPage);
    }

    [Fact]
    public void ModFootprintPage_KeepsAnExplicitChoice()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("""{ "ShowModFootprintPage": true }""");

        Assert.True(settings!.ShowModFootprintPage);
    }

    [Fact]
    public void Theme_IsWrittenAsAName()
    {
        // settings.json is offered for hand-editing on the Options page, where "Theme": 0 would mean
        // nothing to whoever opened it.
        var json = JsonSerializer.Serialize(new AppSettings { Theme = ThemePreference.FollowSystem });

        Assert.Contains("\"FollowSystem\"", json);
    }

    [Theory]
    [InlineData("FollowSystem", ThemePreference.FollowSystem)]
    [InlineData("Light", ThemePreference.Light)]
    [InlineData("Dark", ThemePreference.Dark)]
    public void Theme_ReadsBackEveryName(string name, ThemePreference expected)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>($$"""{ "Theme": "{{name}}" }""");

        Assert.Equal(expected, settings!.Theme);
    }

    [Fact]
    public void Theme_SurvivesARoundTrip()
    {
        var original = new AppSettings
        {
            SptInstallPath = @"C:\SPT",
            DismissedAppUpdateVersion = "1.4.1-beta",
            Theme = ThemePreference.Light,
        };

        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(original));

        Assert.Equal(original.SptInstallPath, restored!.SptInstallPath);
        Assert.Equal(original.DismissedAppUpdateVersion, restored.DismissedAppUpdateVersion);
        Assert.Equal(ThemePreference.Light, restored.Theme);
    }
}
