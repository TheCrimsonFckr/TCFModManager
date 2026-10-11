using System.Text.Json;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

// Named filter presets on Browse and Installed (OPEN-25 S1): the name rules, saving over a name,
// and whether a preset is the page as it is.
public class FilterPresetTests
{
    private static BrowseFilterPreset Browse(string name = "Fika only", string? search = "", params string[] pinned) => new()
    {
        Name = name,
        Filters = new BrowsePageDefaults
        {
            Sort = "Newest",
            Featured = "Include",
            Category = "Bots",
            PageSize = 24,
            Attributes = ["FikaCompatible", "HideAds"],
            SptVersions = ["4.1"],
            Published = "AnyTime",
            Updated = "Last30Days",
            SearchScope = "TitleAndTeaser",
        },
        Search = search,
        Pinned = [.. pinned],
    };

    private static InstalledFilterPreset Installed(string name = "Disabled") => new()
    {
        Name = name,
        Filters = new InstalledPageDefaults
        {
            ViewMode = "List",
            UpdateStatus = "All",
            Enabled = "DisabledOnly",
            Group = "all",
            Sort = "NameAscending",
            GroupSort = "Manual",
            PageSize = 12,
        },
        Search = "",
        Pinned = ["Group"],
    };

    // ---- names -----------------------------------------------------------------------------

    [Theory]
    [InlineData("  Fika   only ", "Fika only")]
    [InlineData("\tBots\n", "Bots")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void A_name_is_trimmed_and_its_spaces_collapsed(string? name, string expected) =>
        Assert.Equal(expected, FilterPresetRules.CleanName(name));

    [Fact]
    public void Names_are_unique_per_page_ignoring_case_and_spacing()
    {
        var presets = new List<BrowseFilterPreset> { Browse("Fika only") };

        Assert.Equal(PresetNameProblem.Taken, FilterPresetRules.Check(presets, "FIKA  ONLY"));
        Assert.Equal(PresetNameProblem.Empty, FilterPresetRules.Check(presets, "  "));
        Assert.Equal(PresetNameProblem.None, FilterPresetRules.Check(presets, "Bots"));

        // Renaming a preset to its own name, in another case, is fine.
        Assert.Equal(PresetNameProblem.None, FilterPresetRules.Check(presets, "fika only", presets[0].Id));
    }

    [Fact]
    public void Saving_a_name_that_exists_replaces_it_in_place_and_keeps_its_id()
    {
        var first = Browse("Bots");
        var presets = new List<BrowseFilterPreset> { first, Browse("Fika only") };

        var saved = FilterPresetRules.Save(presets, Browse(" bots ", search: "sain"));

        Assert.Equal(2, presets.Count);
        Assert.Same(saved, presets[0]);
        Assert.Equal(first.Id, saved.Id);
        Assert.Equal("bots", saved.Name);
        Assert.Equal("sain", saved.Search);
    }

    [Fact]
    public void Saving_a_new_name_adds_it_on_the_end()
    {
        var presets = new List<InstalledFilterPreset> { Installed("Disabled") };

        FilterPresetRules.Save(presets, Installed("  Updates "));

        Assert.Equal(["Disabled", "Updates"], presets.Select(p => p.Name));
    }

    [Fact]
    public void Rename_refuses_a_name_another_preset_has()
    {
        var presets = new List<BrowseFilterPreset> { Browse("Bots"), Browse("Fika only") };

        Assert.False(FilterPresetRules.Rename(presets, presets[1].Id, "bots"));
        Assert.False(FilterPresetRules.Rename(presets, presets[1].Id, " "));
        Assert.True(FilterPresetRules.Rename(presets, presets[1].Id, "  Fika   and ads "));
        Assert.Equal("Fika and ads", presets[1].Name);
    }

    // ---- which preset is the page ----------------------------------------------------------

    [Fact]
    public void A_preset_is_active_when_the_page_means_the_same()
    {
        var preset = Browse(search: "SAIN", pinned: ["Sort", "Category"]);
        var page = Browse(search: " sain ", pinned: ["Category", "Sort", "Presets"]);
        page.Filters.Attributes = ["hideads", "FikaCompatible"];
        page.Filters.Sort = "newest";

        Assert.True(FilterPresetMatch.Matches(preset, page));
    }

    [Fact]
    public void Any_difference_in_a_filter_is_not_the_preset()
    {
        var preset = Browse();

        var page = Browse();
        page.Filters.PageSize = 48;
        Assert.False(FilterPresetMatch.Matches(preset, page));

        page = Browse();
        page.Filters.Attributes = ["FikaCompatible"];
        Assert.False(FilterPresetMatch.Matches(preset, page));

        page = Browse(search: "sain");
        Assert.False(FilterPresetMatch.Matches(preset, page));

        page = Browse(pinned: ["Sort"]);
        Assert.False(FilterPresetMatch.Matches(preset, page));
    }

    [Fact]
    public void Follow_the_install_and_every_SPT_version_are_different_answers()
    {
        var preset = Browse();
        preset.Filters.SptVersions = null;

        var page = Browse();
        page.Filters.SptVersions = [];

        Assert.False(FilterPresetMatch.Matches(preset, page));
    }

    [Fact]
    public void A_custom_range_compares_by_day_and_other_ranges_ignore_days()
    {
        var preset = Browse();
        preset.Filters.Published = "Custom";
        preset.PublishedFrom = new DateTime(2026, 9, 1, 0, 0, 0);
        preset.PublishedUntil = new DateTime(2026, 9, 30);

        var page = Browse();
        page.Filters.Published = "Custom";
        page.PublishedFrom = new DateTime(2026, 9, 1, 15, 30, 0);
        page.PublishedUntil = new DateTime(2026, 9, 30);
        Assert.True(FilterPresetMatch.Matches(preset, page));

        page.PublishedUntil = new DateTime(2026, 10, 1);
        Assert.False(FilterPresetMatch.Matches(preset, page));

        // Last 30 days has no days of its own; stray ones on the page don't matter.
        var thirty = Browse();
        var other = Browse();
        other.UpdatedFrom = new DateTime(2020, 1, 1);
        Assert.True(FilterPresetMatch.Matches(thirty, other));
    }

    [Fact]
    public void A_hand_edited_preset_without_search_or_pins_leaves_them_out_of_it()
    {
        var preset = Browse();
        preset.Search = null;
        preset.Pinned = null;

        Assert.True(FilterPresetMatch.Matches(preset, Browse(search: "anything", pinned: ["Sort", "Featured"])));
    }

    [Fact]
    public void The_first_matching_preset_is_the_active_one()
    {
        var bots = Installed("Bots");
        var disabled = Installed("Disabled");
        var list = Installed("List");
        list.Filters.ViewMode = "Cards";

        var page = Installed("(page)");

        Assert.Same(bots, FilterPresetMatch.Active([list, bots, disabled], page));
        Assert.Null(FilterPresetMatch.Active([list], page));
    }

    [Fact]
    public void Installed_view_mode_is_part_of_the_preset()
    {
        var preset = Installed();
        var page = Installed();
        page.Filters.ViewMode = "Groups";

        Assert.False(FilterPresetMatch.Matches(preset, page));
    }

    // ---- settings.json ---------------------------------------------------------------------

    [Fact]
    public void Presets_round_trip_through_settings_and_old_files_have_none()
    {
        var settings = new AppSettings();
        settings.BrowseFilterPresets.Add(Browse(search: "sain", pinned: ["Sort"]));
        settings.InstalledFilterPresets.Add(Installed());

        var back = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Assert.True(FilterPresetMatch.Matches(settings.BrowseFilterPresets[0], back.BrowseFilterPresets[0]));
        Assert.Equal(settings.BrowseFilterPresets[0].Id, back.BrowseFilterPresets[0].Id);
        Assert.Equal("Disabled", back.InstalledFilterPresets[0].Name);

        var old = JsonSerializer.Deserialize<AppSettings>("{}")!;
        Assert.Empty(old.BrowseFilterPresets);
        Assert.Empty(old.InstalledFilterPresets);
    }
}
