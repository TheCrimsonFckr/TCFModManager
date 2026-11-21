using TCFModManager.App.Localization;
using TCFModManager.App.ViewModels;
using TCFModManager.Core.Models;

namespace TCFModManager.App.Services;

public enum CardFieldKind
{
    Inline,
    Line,
    Chip,
    Block,
    Button,
}

public sealed record CardFieldSpec(string Key, CardFieldKind Kind, string LabelKey, Func<object, string?>? Text = null, bool IsAuthorLink = false)
{
    public string Label => LocalizationService.Get(LabelKey);

    public string? TextFor(object? item) => item is null || Text is null ? null : Text(item);
}

public static class CardLayoutFields
{
    public const string Note = "Note";
    public const string Version = "Version";
    public const string Sides = "Sides";
    public const string Author = "Author";
    public const string AddonParent = "AddonParent";
    public const string ModLists = "ModLists";
    public const string Group = "Group";
    public const string Disabled = "Disabled";
    public const string Pinned = "Pinned";
    public const string DisableToggle = "DisableToggle";
    public const string QuickUpdate = "QuickUpdate";
    public const string Category = "Category";
    public const string Downloads = "Downloads";
    public const string Updated = "Updated";
    public const string InstalledOn = "InstalledOn";
    public const string SptFit = "SptFit";
    public const string Teaser = "Teaser";

    public const string Guid = "Guid";
    public const string Endorsements = "Endorsements";
    public const string MatchedAddons = "MatchedAddons";
    public const string SptVersion = "SptVersion";
    public const string AlternateNote = "AlternateNote";
    public const string Badges = "Badges";

    private static InstalledModCardViewModel? I(object o) => o as InstalledModCardViewModel;

    private static ModCardViewModel? B(object o) => o as ModCardViewModel;

    private static string? Date(DateTimeOffset? at) => at?.ToLocalTime().ToString("d");

    public static IReadOnlyList<CardFieldSpec> Installed { get; } =
    [
        new(Note, CardFieldKind.Block, nameof(Strings.CardField_Note), o => I(o)?.Note),
        new(Version, CardFieldKind.Line, nameof(Strings.CardField_Version), o => I(o)?.InstalledVersion ?? Strings.Common_Unknown),
        new(Sides, CardFieldKind.Inline, nameof(Strings.CardField_Sides), o => I(o)?.TargetSummary),
        new(Author, CardFieldKind.Line, nameof(Strings.CardField_Author), o => I(o)?.Author ?? Strings.Common_Unknown, IsAuthorLink: true),
        new(AddonParent, CardFieldKind.Chip, nameof(Strings.CardField_AddonParent), o => I(o)?.AddonSubtitle),
        new(ModLists, CardFieldKind.Chip, nameof(Strings.CardField_ModLists)),
        new(Group, CardFieldKind.Chip, nameof(Strings.CardField_Group)),
        new(Disabled, CardFieldKind.Chip, nameof(Strings.CardField_Disabled)),
        new(Pinned, CardFieldKind.Chip, nameof(Strings.CardField_Pinned)),
        new(DisableToggle, CardFieldKind.Button, nameof(Strings.CardField_DisableToggle)),
        new(QuickUpdate, CardFieldKind.Button, nameof(Strings.CardField_QuickUpdate)),
        new(Category, CardFieldKind.Line, nameof(Strings.CardField_Category), o => I(o)?.CategoryTag),
        new(Downloads, CardFieldKind.Line, nameof(Strings.CardField_Downloads),
            o => I(o)?.PreviewDownloads is { } n ? LocalizationService.Text(Strings.Common_DownloadsFormat, n) : null),
        new(Updated, CardFieldKind.Line, nameof(Strings.CardField_Updated),
            o => Date(I(o)?.LatestUpdatedAt) is { } d ? LocalizationService.Text(Strings.Card_UpdatedFormat, d) : null),
        new(InstalledOn, CardFieldKind.Line, nameof(Strings.CardField_InstalledOn),
            o => Date(I(o)?.InstalledAt) is { } d ? LocalizationService.Text(Strings.Card_InstalledOnFormat, d) : null),
        new(SptFit, CardFieldKind.Block, nameof(Strings.CardField_SptFit), o => I(o)?.SptFitLine),
        new(Teaser, CardFieldKind.Block, nameof(Strings.CardField_Teaser), o => I(o)?.PreviewTeaser),
    ];

    public static IReadOnlyList<string> InstalledDefault { get; } =
        [Note, Version, Sides, Author, AddonParent, ModLists, Group, Disabled, Pinned, DisableToggle, QuickUpdate];

    public static IReadOnlyList<CardFieldSpec> Browse { get; } =
    [
        new(Author, CardFieldKind.Block, nameof(Strings.CardField_Author), o => B(o)?.Author),
        new(Guid, CardFieldKind.Block, nameof(Strings.CardField_Guid), o => B(o)?.Guid),
        new(Downloads, CardFieldKind.Block, nameof(Strings.CardField_Downloads)),
        new(Endorsements, CardFieldKind.Block, nameof(Strings.CardField_Endorsements)),
        new(MatchedAddons, CardFieldKind.Block, nameof(Strings.CardField_MatchedAddons)),
        new(SptVersion, CardFieldKind.Block, nameof(Strings.CardField_SptVersion)),
        new(AlternateNote, CardFieldKind.Block, nameof(Strings.CardField_AlternateNote)),
        new(Badges, CardFieldKind.Block, nameof(Strings.CardField_Badges)),
    ];

    public static IReadOnlyList<string> BrowseDefault { get; } =
        [Author, Guid, Downloads, Endorsements, MatchedAddons, SptVersion, AlternateNote, Badges];

    public static IReadOnlyList<CardFieldSpec> For(CardLayoutPage page) => page == CardLayoutPage.Browse ? Browse : Installed;

    public static IReadOnlyList<string> DefaultFor(CardLayoutPage page) => page == CardLayoutPage.Browse ? BrowseDefault : InstalledDefault;

    public static IReadOnlySet<string> KnownFor(CardLayoutPage page) => For(page).Select(f => f.Key).ToHashSet(StringComparer.Ordinal);

    public static CardPicture DefaultPicture(CardLayoutPage page) => page == CardLayoutPage.Browse ? CardPicture.Small : CardPicture.Off;

    public static CardLayout DefaultLayout(CardLayoutPage page) => new()
    {
        Id = System.Guid.Empty,
        Name = Strings.CardLayout_Default,
        Page = page,
        Fields = [.. DefaultFor(page)],
        ListFields = page == CardLayoutPage.Installed ? [.. InstalledDefault.Where(f => f != AddonParent)] : null,
        Picture = DefaultPicture(page),
    };
}
