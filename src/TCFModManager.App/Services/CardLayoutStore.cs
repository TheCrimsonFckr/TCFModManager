using TCFModManager.App.Localization;
using TCFModManager.App.ViewModels;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

public sealed class CardLayoutStore
{
    private readonly SettingsService _settings = new();
    private List<CardLayout> _all;

    public CardLayoutStore()
    {
        _all = Load();
    }

    public event EventHandler? Changed;

    public IReadOnlyList<CardLayout> All => _all;

    public IReadOnlyList<CardLayout> ForPage(CardLayoutPage page) => [.. CardLayoutRules.ForPage(_all, page)];

    public IReadOnlyList<CardLayoutView> ChoicesFor(CardLayoutPage page) =>
        [CardLayoutView.Default(page), .. ForPage(page).Select(l => new CardLayoutView(l, isDefault: false))];

    public CardLayoutView Resolve(CardLayoutPage page, string? id) =>
        CardLayoutRules.Find(_all, page, id) is { } layout ? new CardLayoutView(layout, isDefault: false) : CardLayoutView.Default(page);

    public CardLayout? Find(CardLayoutPage page, Guid id) => CardLayoutRules.Find(_all, page, id);

    public void Save(CardLayout layout)
    {
        var copy = CardLayoutRules.Copy(layout, layout.Name);
        copy.Id = layout.Id;
        CardLayoutRules.Normalise(copy, CardLayoutFields.KnownFor(copy.Page), _all, Strings.CardLayout_NewName);
        copy.FieldsRevision = CardLayoutFields.Revision;

        var next = _all.Where(l => l.Id != copy.Id).ToList();
        var at = _all.FindIndex(l => l.Id == copy.Id);
        if (at < 0) next.Add(copy);
        else next.Insert(Math.Min(at, next.Count), copy);

        Write(next);
        AppLog.Info("CardLayouts", $"{(at < 0 ? "added" : "saved")} {copy.Page} layout \"{copy.Name}\" ({copy.Fields.Count} field(s), {copy.Size}, {copy.Width}, picture {copy.Picture})");
    }

    public void Delete(Guid id)
    {
        var gone = _all.FirstOrDefault(l => l.Id == id);
        if (gone is null) return;

        Write([.. _all.Where(l => l.Id != id)]);
        AppLog.Info("CardLayouts", $"deleted {gone.Page} layout \"{gone.Name}\"");
    }

    private void Write(List<CardLayout> next)
    {
        _all = next;
        var settings = _settings.Load();
        settings.CardLayouts = [.. next.Select(l => { var c = CardLayoutRules.Copy(l, l.Name); c.Id = l.Id; return c; })];
        _settings.Save(settings);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private List<CardLayout> Load()
    {
        var loaded = _settings.Load().CardLayouts ?? [];
        var kept = new List<CardLayout>();

        foreach (var layout in loaded.Where(l => l is not null && Enum.IsDefined(l.Page)))
        {
            if (layout.Id == Guid.Empty || kept.Any(k => k.Id == layout.Id)) layout.Id = Guid.NewGuid();
            if (layout.FieldsRevision is null) Upgrade(layout);
            CardLayoutRules.Normalise(layout, CardLayoutFields.KnownFor(layout.Page), kept, Strings.CardLayout_NewName);
            if (CardLayoutRules.CheckName(kept, layout.Page, layout.Name) == PresetNameProblem.Taken)
                layout.Name = CardLayoutRules.UniqueName(kept, layout.Page, layout.Name);
            kept.Add(layout);
        }

        if (kept.Count > 0) AppLog.Info("CardLayouts", $"loaded {kept.Count} layout(s)");
        return kept;
    }

    private static void Upgrade(CardLayout layout)
    {
        if (layout.Page == CardLayoutPage.Installed)
        {
            layout.Fields = [.. layout.Fields ?? [], .. CardLayoutFields.AddedInRevision1];
            if (layout.ListFields is not null) layout.ListFields = [.. layout.ListFields, .. CardLayoutFields.AddedInRevision1];
        }

        layout.FieldsRevision = CardLayoutFields.Revision;
    }
}
