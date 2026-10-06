namespace TCFModManager.Core.Services;

//
// OPEN-12 F19: Browse's Published and Updated filters - is a moment within "from this day until that
// day"? Days are whole days in the PC's own time zone, the "until" day included; either end may be
// left open. A mod with no date at all is only kept while both ends are open.
//
// From SSPTMM's DateRangeFilter, plus the presets Chris asked for (R20): last 7, 30 and 90 days and
// last year, counted back from today, or a custom pair of days.
//
public enum DateRangePreset
{
    AnyTime,
    Last7Days,
    Last30Days,
    Last90Days,
    LastYear,
    Custom,
}

public static class DateRangeFilter
{
    public static bool Contains(DateTimeOffset? when, DateTime? from, DateTime? until, TimeZoneInfo? zone = null)
    {
        if (from is null && until is null) return true;
        if (when is null) return false;

        var local = TimeZoneInfo.ConvertTime(when.Value, zone ?? TimeZoneInfo.Local).DateTime;
        return (from is null || local >= from.Value.Date)
            && (until is null || local < until.Value.Date.AddDays(1));
    }

    //
    // The two ends a preset stands for on the given day. A preset counts today as one of its days:
    // "last 7 days" on the 10th is the 4th to the 10th. Custom takes the days it is given, either of
    // which may be open; a pair given the wrong way round is swapped rather than matching nothing.
    //
    public static (DateTime? From, DateTime? Until) Window(
        DateRangePreset preset, DateTime today, DateTime? customFrom = null, DateTime? customUntil = null)
    {
        var day = today.Date;
        return preset switch
        {
            DateRangePreset.Last7Days => (day.AddDays(-6), null),
            DateRangePreset.Last30Days => (day.AddDays(-29), null),
            DateRangePreset.Last90Days => (day.AddDays(-89), null),
            DateRangePreset.LastYear => (day.AddYears(-1).AddDays(1), null),
            DateRangePreset.Custom when customFrom is { } a && customUntil is { } b && a.Date > b.Date => (b.Date, a.Date),
            DateRangePreset.Custom => (customFrom?.Date, customUntil?.Date),
            _ => (null, null),
        };
    }

    // Whether the preset narrows anything - a custom range with neither end set does not.
    public static bool IsActive(DateRangePreset preset, DateTime? customFrom, DateTime? customUntil) =>
        preset switch
        {
            DateRangePreset.AnyTime => false,
            DateRangePreset.Custom => customFrom is not null || customUntil is not null,
            _ => true,
        };
}
