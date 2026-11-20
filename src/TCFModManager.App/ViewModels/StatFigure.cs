using TCFModManager.App.Localization;

namespace TCFModManager.App.ViewModels;

// A stat box figure split into its leading number and the rest, so the number can sit in a fixed slot of its own.
public sealed record StatFigure(string Number, string Rest)
{
    public static StatFigure None { get; } = new(string.Empty, "—");

    public bool HasNumber => Number.Length > 0;

    public static StatFigure From(string format, params object?[] values)
    {
        if (!format.StartsWith("{0", StringComparison.Ordinal) || format.IndexOf('}') is not (> 0 and var close))
            return new(string.Empty, LocalizationService.Text(format, values));

        return new(LocalizationService.Text(format[..(close + 1)], values), LocalizationService.Text(format[(close + 1)..], values));
    }

    // This figure as {0} of <paramref name="format"/>: "{0}/s" keeps the number and makes the rest " MB/s".
    public StatFigure Within(string format, params object?[] more) =>
        format.StartsWith("{0}", StringComparison.Ordinal)
            ? new(Number, LocalizationService.Text(format, [Rest, .. more]))
            : new(string.Empty, LocalizationService.Text(format, [ToString(), .. more]));

    public override string ToString() => Number + Rest;
}
