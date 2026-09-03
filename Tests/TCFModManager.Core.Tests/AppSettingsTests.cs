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
    public void ViewPreferences_AreAbsentUntilSomebodyChangesOne()
    {
        // Null is what tells each page to use its own default, so a settings file written before
        // these existed has to leave them null rather than land on a number of its own. An upgrade
        // must open exactly the way the previous version did.
        var fresh = new AppSettings();
        var upgraded = JsonSerializer.Deserialize<AppSettings>("""{ "SptInstallPath": "C:\\SPT" }""");

        Assert.Null(fresh.InstalledPageSize);
        Assert.Null(fresh.InstalledSort);
        Assert.Null(fresh.BrowsePageSize);
        Assert.Null(fresh.BrowseSort);

        Assert.Null(upgraded!.InstalledPageSize);
        Assert.Null(upgraded.InstalledSort);
    }

    [Fact]
    public void ViewPreferences_KeepInstalledAndBrowseApart()
    {
        // The two pages hold different things and get sorted differently, so settling on 30 per
        // page in Browse must not reach over to Installed.
        var json = """
            {
              "InstalledPageSize": 6,
              "InstalledSort": "RecentlyInstalled",
              "BrowsePageSize": 30,
              "BrowseSort": "MostDownloaded"
            }
            """;

        var settings = JsonSerializer.Deserialize<AppSettings>(json);

        Assert.Equal(6, settings!.InstalledPageSize);
        Assert.Equal("RecentlyInstalled", settings.InstalledSort);
        Assert.Equal(30, settings.BrowsePageSize);
        Assert.Equal("MostDownloaded", settings.BrowseSort);
    }

    [Fact]
    public void ViewPreferences_AreWrittenAsNamesToo()
    {
        // Same reason as Theme: this file is offered for hand-editing, and a number here would be
        // unreadable. The orderings live in the App project, so these are stored as plain strings
        // and resolved back by name there.
        var json = JsonSerializer.Serialize(new AppSettings
        {
            InstalledSort = "NameDescending",
            BrowseSort = "Newest",
        });

        Assert.Contains("\"NameDescending\"", json);
        Assert.Contains("\"Newest\"", json);
    }

    [Fact]
    public void ViewPreferences_SurviveARoundTrip()
    {
        var original = new AppSettings
        {
            InstalledPageSize = 21,
            InstalledSort = "AuthorAscending",
            BrowsePageSize = 9,
            BrowseSort = "MostEndorsed",
        };

        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(original));

        Assert.Equal(21, restored!.InstalledPageSize);
        Assert.Equal("AuthorAscending", restored.InstalledSort);
        Assert.Equal(9, restored.BrowsePageSize);
        Assert.Equal("MostEndorsed", restored.BrowseSort);
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
