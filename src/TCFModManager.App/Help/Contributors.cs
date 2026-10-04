using TCFModManager.App.Localization;

namespace TCFModManager.App.Help;

//
// Someone who has contributed to the app, for the Credits window.
//
// Name is a handle as the person gave it and is never translated. Contribution is a key, so what
// they did reads in the user's language. Link is optional - only where they asked for one.
//
public sealed record Contributor(string Name, Func<string> Contribution, string? Link = null);

//
// Everyone in the Credits window, in the order they contributed - only the people Chris names.
// Adding a person is one line here and one string key in each language.
//
public static class Contributors
{
    public static IReadOnlyList<Contributor> All { get; } =
    [
        new("brunolz13", () => Strings.Credits_Brunolz13),
    ];
}
