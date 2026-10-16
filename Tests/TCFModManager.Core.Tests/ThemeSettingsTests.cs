using System.Text.Json;
using TCFModManager.Core.Models;
using Xunit;

namespace TCFModManager.Core.Tests;

public class ThemeSettingsTests
{
    [Fact]
    public void ThemeId_IsAbsentUntilAThemeIsPicked()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("""{ "Theme": "Dark" }""");

        Assert.Null(settings!.ThemeId);
        Assert.Equal(ThemePreference.Dark, settings.Theme);
    }

    [Fact]
    public void ThemeId_RoundTrips()
    {
        var json = JsonSerializer.Serialize(new AppSettings { ThemeId = "steam" });

        Assert.Equal("steam", JsonSerializer.Deserialize<AppSettings>(json)!.ThemeId);
    }
}
