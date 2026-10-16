using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;

namespace TCFModManager.App.ViewModels;

// One ignored mod in Options.
public sealed class IgnoredModRow(int id, string name, string? author, string? url)
{
    public int Id { get; } = id;

    public string Name { get; } = name;

    public string? Detail => author is null ? null : LocalizationService.Text(Strings.Browse_ByAuthorFormat, author);

    public string? Url { get; } = url;

    public bool HasUrl => !string.IsNullOrEmpty(Url);
}

//
// OPEN-30: the mods the user ignored, listed on the Options page - each opens its sp-mod page or is
// un-ignored there. Rebuilt whenever a mod is ignored or un-ignored, wherever that happened.
//
public sealed partial class IgnoredModsViewModel : LocalizedViewModel
{
    public ObservableCollection<IgnoredModRow> Mods { get; } = [];

    public bool IsEmpty => Mods.Count == 0;

    public string Summary => LocalizationService.Text(Strings.Ignored_SummaryFormat, Mods.Count);

    public IgnoredModsViewModel()
    {
        AppServices.Ignored.Changed += (_, _) => Refresh();
        Refresh();
    }

    public void Refresh()
    {
        var catalog = AppServices.ModCache.AllMods.ToDictionary(m => m.Id);

        Mods.Clear();
        foreach (var ignored in AppServices.Ignored.All.OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            catalog.TryGetValue(ignored.Id, out var mod);
            Mods.Add(new IgnoredModRow(
                ignored.Id,
                mod?.Name ?? ignored.Name ?? ignored.Id.ToString(),
                mod?.Owner?.Name,
                mod?.DetailUrl));
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(Summary));
    }

    protected internal override void RefreshText()
    {
        base.RefreshText();
        Refresh();
    }

    [RelayCommand]
    private void Open(IgnoredModRow? row)
    {
        if (row?.Url is { Length: > 0 } url) MarkupActions.OpenInBrowser(url);
    }

    [RelayCommand]
    private void StopIgnoring(IgnoredModRow? row)
    {
        if (row is not null) AppServices.Ignored.Set(row.Id, row.Name, ignore: false);
    }
}
