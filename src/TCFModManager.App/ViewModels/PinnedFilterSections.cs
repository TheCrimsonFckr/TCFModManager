using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

//
// Which of a page's Filters panel sections are pinned to the row under the search bar (OPEN-24).
// One per page view model, exposed as FilterPins; the panel's section headers bind their pin button to
// Toggle and read Names (see Behaviors/FilterPin), and the row under the search bar shows Bar.
//
// A layout preference, saved the moment it changes and kept across launches. Clear filters,
// Restore my defaults and Save as default never touch it.
//
public sealed partial class PinnedFilterSections<TSection> : ObservableObject
    where TSection : struct, Enum
{
    private static readonly string[] Known = Enum.GetNames<TSection>();

    private readonly string _page;
    private readonly Action<AppSettings, List<string>?> _write;
    private readonly object _owner;
    private readonly Func<TSection, string?> _label;
    private readonly Func<TSection, bool>? _shown;

    //
    // `label` gives the text on a section's dropdown in the bar; it may return null while the page
    // view model is still being built, and the label is filled in by the next RefreshLabels. `shown`,
    // when given, hides a section's dropdown while the page's panel hides that section.
    //
    public PinnedFilterSections(
        string page,
        AppSettings settings,
        Func<AppSettings, List<string>?> read,
        Action<AppSettings, List<string>?> write,
        object owner,
        Func<TSection, string?> label,
        Func<TSection, bool>? shown = null)
    {
        _page = page;
        _write = write;
        _owner = owner;
        _label = label;
        _shown = shown;
        _names = PinnedFilters.Normalise(read(settings), Known);
        SyncBar();
    }

    //
    // The dropdowns in the row under the search bar, in panel order. Kept in step with Names item by
    // item rather than rebuilt, so a dropdown that is open stays open while its label changes.
    //
    public ObservableCollection<PinnedFilterBarItem> Bar { get; } = [];

    // Rewrites each dropdown's label from the page's current filters, and whether it shows. Cheap;
    // the page calls it whenever any of its properties change.
    public void RefreshLabels()
    {
        foreach (var item in Bar)
        {
            var section = Enum.Parse<TSection>(item.Section);
            if (_label(section) is { } text && text != item.Label) item.Label = text;
            if (_shown is not null) item.IsShown = _shown(section);
        }
    }

    partial void OnNamesChanged(IReadOnlyList<string> value) => SyncBar();

    private void SyncBar()
    {
        for (var i = Bar.Count - 1; i >= 0; i--)
        {
            if (!Names.Contains(Bar[i].Section)) Bar.RemoveAt(i);
        }

        for (var i = 0; i < Names.Count; i++)
        {
            if (i < Bar.Count && Bar[i].Section == Names[i]) continue;
            Bar.Insert(i, new PinnedFilterBarItem(Names[i], _owner, ToggleCommand));
        }

        RefreshLabels();
    }

    //
    // The pinned section names in panel order. Replaced rather than changed in place, so a binding
    // to it hears every pin and unpin.
    //
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Sections))]
    [NotifyPropertyChangedFor(nameof(Any))]
    private IReadOnlyList<string> _names;

    public IReadOnlyList<TSection> Sections => Names.Select(Enum.Parse<TSection>).ToList();

    public bool Any => Names.Count > 0;

    public bool IsPinned(TSection section) => Names.Contains(section.ToString());

    //
    // A filter preset's pins (OPEN-25 R3), saved like a toggle. Null - a hand-edited preset that
    // names none - leaves them alone. The Filter presets section's own pin is never touched (R6):
    // switching presets from the pinned presets dropdown must not take that dropdown away.
    //
    public void ApplyPreset(IReadOnlyList<string>? pinned)
    {
        if (pinned is null) return;

        var presets = Core.Services.FilterPresetMatch.PresetsSection;
        var keepPresets = Names.Contains(presets);
        var wanted = pinned.Where(n => !string.Equals(n, presets, StringComparison.OrdinalIgnoreCase))
            .Concat(keepPresets ? [presets] : []);
        var names = PinnedFilters.Normalise(wanted, Known);
        if (names.SequenceEqual(Names)) return;

        Names = names;

        var service = new SettingsService();
        var current = service.Load();
        _write(current, Names.Count > 0 ? Names.ToList() : null);
        service.Save(current);
    }

    // The pins as a preset keeps them - without the Filter presets section's own (R6).
    public List<string> ForPreset() =>
        Names.Where(n => n != Core.Services.FilterPresetMatch.PresetsSection).ToList();

    [RelayCommand]
    private void Toggle(string? section)
    {
        if (!Enum.TryParse<TSection>(section, out var parsed) || !Enum.IsDefined(parsed)) return;

        var name = parsed.ToString();
        var pin = !Names.Contains(name);
        var wanted = pin ? Names.Append(name) : Names.Where(n => n != name);
        Names = PinnedFilters.Normalise(wanted, Known);

        var service = new SettingsService();
        var current = service.Load();
        _write(current, Names.Count > 0 ? Names.ToList() : null);
        service.Save(current);

        AppLog.Info(_page, $"{(pin ? "pinned" : "unpinned")} the {name} filter");
    }
}
