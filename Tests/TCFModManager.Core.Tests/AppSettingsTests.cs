using System.Text.Json;
using TCFModManager.Core.Models;
using Xunit;

namespace TCFModManager.Core.Tests;

public class AppSettingsTests
{
    [Fact]
    public void Theme_DefaultsToDarkWhenTheFileHasNeverHeardOfIt()
    {
        // Every build before the setting existed was Dark, so an upgrading install has to stay Dark
        // rather than quietly switching to whatever Windows is set to.
        var settings = JsonSerializer.Deserialize<AppSettings>("""{ "SptInstallPath": "C:\\SPT" }""");

        Assert.NotNull(settings);
        Assert.Equal(ThemePreference.Dark, settings!.Theme);
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
