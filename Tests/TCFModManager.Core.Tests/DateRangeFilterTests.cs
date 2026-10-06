using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

//
// OPEN-12 F19: the date filters on Browse. The first five are SSPTMM's own; the rest cover the
// presets added here.
//
public class DateRangeFilterTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    private static DateTimeOffset At(int month, int day, int hour = 12) => new(2026, month, day, hour, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Open_at_both_ends_keeps_everything_even_undated()
    {
        Assert.True(DateRangeFilter.Contains(null, null, null, Utc));
        Assert.True(DateRangeFilter.Contains(At(1, 1), null, null, Utc));
    }

    [Fact]
    public void An_undated_mod_drops_out_once_a_range_is_set()
    {
        Assert.False(DateRangeFilter.Contains(null, new DateTime(2026, 1, 1), null, Utc));
    }

    [Fact]
    public void The_until_day_counts_in_full()
    {
        var until = new DateTime(2026, 9, 24);

        Assert.True(DateRangeFilter.Contains(At(9, 24, 23), null, until, Utc));
        Assert.False(DateRangeFilter.Contains(At(9, 25, 0), null, until, Utc));
    }

    [Fact]
    public void The_from_day_starts_at_midnight()
    {
        var from = new DateTime(2026, 9, 24);

        Assert.True(DateRangeFilter.Contains(At(9, 24, 0), from, null, Utc));
        Assert.False(DateRangeFilter.Contains(At(9, 23, 23), from, null, Utc));
    }

    [Fact]
    public void Days_are_the_pcs_own()
    {
        // 23:30 UTC on the 23rd is already the 24th two hours east.
        var east = TimeZoneInfo.CreateCustomTimeZone("east", TimeSpan.FromHours(2), "east", "east");
        var when = new DateTimeOffset(2026, 9, 23, 23, 30, 0, TimeSpan.Zero);

        Assert.True(DateRangeFilter.Contains(when, new DateTime(2026, 9, 24), null, east));
        Assert.False(DateRangeFilter.Contains(when, new DateTime(2026, 9, 24), null, Utc));
    }

    [Fact]
    public void A_preset_counts_today_as_one_of_its_days()
    {
        var today = new DateTime(2026, 10, 10, 15, 0, 0);

        Assert.Equal((new DateTime(2026, 10, 4), (DateTime?)null), DateRangeFilter.Window(DateRangePreset.Last7Days, today));
        Assert.Equal((new DateTime(2026, 9, 11), (DateTime?)null), DateRangeFilter.Window(DateRangePreset.Last30Days, today));
        Assert.Equal((new DateTime(2026, 7, 13), (DateTime?)null), DateRangeFilter.Window(DateRangePreset.Last90Days, today));
        Assert.Equal((new DateTime(2025, 10, 11), (DateTime?)null), DateRangeFilter.Window(DateRangePreset.LastYear, today));
        Assert.Equal(((DateTime?)null, (DateTime?)null), DateRangeFilter.Window(DateRangePreset.AnyTime, today));
    }

    [Fact]
    public void A_custom_range_given_backwards_is_swapped()
    {
        var (from, until) = DateRangeFilter.Window(DateRangePreset.Custom, DateTime.Today,
            new DateTime(2026, 9, 30), new DateTime(2026, 9, 1));

        Assert.Equal(new DateTime(2026, 9, 1), from);
        Assert.Equal(new DateTime(2026, 9, 30), until);
    }

    [Fact]
    public void A_custom_range_with_no_days_narrows_nothing()
    {
        Assert.False(DateRangeFilter.IsActive(DateRangePreset.Custom, null, null));
        Assert.True(DateRangeFilter.IsActive(DateRangePreset.Custom, new DateTime(2026, 9, 1), null));
        Assert.False(DateRangeFilter.IsActive(DateRangePreset.AnyTime, new DateTime(2026, 9, 1), null));
        Assert.True(DateRangeFilter.IsActive(DateRangePreset.Last7Days, null, null));
    }
}
