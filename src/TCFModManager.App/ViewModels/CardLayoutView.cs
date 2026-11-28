using TCFModManager.App.Services;
using TCFModManager.Core.Models;
using F = TCFModManager.App.Services.CardLayoutFields;

namespace TCFModManager.App.ViewModels;

public sealed class CardLayoutView
{
    private static readonly HashSet<string> RowOwnColumns = [F.Group, F.Disabled, F.Pinned];

    private readonly HashSet<string> _fields;
    private readonly HashSet<string> _listFields;

    public CardLayoutView(CardLayout layout, bool isDefault)
    {
        Layout = layout;
        IsDefault = isDefault;

        var catalog = F.For(layout.Page).ToDictionary(f => f.Key);
        var shown = layout.Fields.Where(catalog.ContainsKey).Select(k => catalog[k]).ToList();
        var listShown = (layout.ListFields ?? layout.Fields).Where(catalog.ContainsKey).Select(k => catalog[k]).ToList();

        _fields = shown.Select(f => f.Key).ToHashSet();
        _listFields = listShown.Select(f => f.Key).ToHashSet();

        Fields = [.. shown.Where(f => f.Kind != CardFieldKind.Button)];
        RowFields = [.. Fields.Where(f => !RowOwnColumns.Contains(f.Key))];
        ListFields = [.. listShown.Where(f => f.Kind != CardFieldKind.Button && !RowOwnColumns.Contains(f.Key))];
    }

    public static CardLayoutView Default(CardLayoutPage page) => new(F.DefaultLayout(page), isDefault: true);

    public CardLayout Layout { get; }

    public bool IsDefault { get; }

    public Guid? Id => IsDefault ? null : Layout.Id;

    public string Name => IsDefault ? Localization.Strings.CardLayout_Default : Layout.Name;

    public CardLayoutPage Page => Layout.Page;

    public IReadOnlyList<CardFieldSpec> Fields { get; }

    public IReadOnlyList<CardFieldSpec> RowFields { get; }

    public IReadOnlyList<CardFieldSpec> ListFields { get; }

    public bool Has(string key) => _fields.Contains(key);

    public bool ListHas(string key) => _listFields.Contains(key);

    public bool ShowDisableToggle => Has(F.DisableToggle);

    public bool ShowQuickUpdate => Has(F.QuickUpdate);

    public bool ShowPinButton => Has(F.PinButton);

    public bool ShowRemoveButton => Has(F.RemoveButton);

    public bool ShowFolderButtons => Has(F.FolderButtons);

    public bool ShowInstalledAddons => Has(F.InstalledAddons);

    public bool ShowNeededBy => Has(F.NeededBy);

    public bool ShowSpModActions => Has(F.SpModActions);

    public bool RowShowsDisabled => Has(F.Disabled);

    public bool RowShowsPinned => Has(F.Pinned);

    public bool ListShowsDisableToggle => ListHas(F.DisableToggle);

    public bool ListShowsQuickUpdate => ListHas(F.QuickUpdate);

    public bool ListShowsGroup => ListHas(F.Group);

    public bool ListShowsDisabled => ListHas(F.Disabled);

    public bool ListShowsPinned => ListHas(F.Pinned);

    public bool ListShowsInstalledAddons => ListHas(F.InstalledAddons);

    public bool ListShowsNeededBy => ListHas(F.NeededBy);

    public bool ListShowsSpModActions => ListHas(F.SpModActions);

    public CardSize Size => Layout.Size;

    public CardWidth Width => Layout.Width;

    public CardPicture Picture => Layout.Picture;

    public bool ShowSmallPicture => Picture == CardPicture.Small;

    public bool ShowBanner => Picture == CardPicture.Banner;

    public bool DetailsInWindow => Page == CardLayoutPage.Installed && Layout.DetailsInWindow;

    public bool DetailsInDropdown => !DetailsInWindow;

    // OPEN-77: an Installed card's buttons down its left side, or (the default) in a row along its bottom.
    public bool ButtonsAtSide => Page == CardLayoutPage.Installed && Layout.Buttons == CardButtons.Side;

    public bool ButtonsAtBottom => Page == CardLayoutPage.Installed && !ButtonsAtSide;

    public System.Windows.Controls.Orientation ButtonsOrientation =>
        ButtonsAtSide ? System.Windows.Controls.Orientation.Vertical : System.Windows.Controls.Orientation.Horizontal;

    public double WidthFactor => Width switch
    {
        CardWidth.Narrow => 0.75,
        CardWidth.Wide => 1.4,
        _ => 1.0,
    };

    public double HeaderMinHeight
    {
        get
        {
            var (title, line, chip) = Behaviors.CardDensity.FontSizes(Size);
            var gap = Behaviors.CardDensity.LineGap(Size);

            var buttons = 26 + (ButtonsAtSide ? (ShowDisableToggle ? 34 : 0) + (ShowQuickUpdate ? 32 : 0) + (ShowPinButton ? 32 : 0) + (ShowRemoveButton ? 32 : 0) : 0);

            var inlineRuns = Fields.Count(f => f.Kind == CardFieldKind.Line);
            var chipRuns = 0;
            CardFieldSpec? last = null;
            foreach (var spec in Fields)
            {
                if (spec.Kind != last?.Kind && spec.Kind == CardFieldKind.Inline) inlineRuns++;
                if (spec.Kind == CardFieldKind.Chip && (last?.Kind != CardFieldKind.Chip || spec.OwnRow || last.OwnRow)) chipRuns++;
                last = spec;
            }

            var text = title * 1.33 + inlineRuns * (line * 1.33 + gap) + chipRuns * (chip * 1.33 + 6 + 4 + gap);
            return Math.Ceiling(Math.Max(buttons, text));
        }
    }

    public override string ToString() => Name;
}
