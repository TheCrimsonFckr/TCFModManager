using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

// One followed author in Options.
public sealed class FollowedAuthorRow(int id, string name, int mods)
{
    public int Id { get; } = id;

    public string Name { get; } = name;

    public string Detail => LocalizationService.Text(Strings.Author_ModCountFormat, mods);
}

//
// OPEN-12 A4 (R23): the authors the user follows, listed on the Options page - each opens its page
// or is unfollowed there. Rebuilt whenever anyone is followed or unfollowed, wherever that happened.
//
public sealed partial class FollowedAuthorsViewModel : LocalizedViewModel
{
    public ObservableCollection<FollowedAuthorRow> Authors { get; } = [];

    public bool IsEmpty => Authors.Count == 0;

    public string Summary => LocalizationService.Text(Strings.Followed_SummaryFormat, Authors.Count);

    public FollowedAuthorsViewModel()
    {
        AppServices.Followed.Changed += (_, _) => Refresh();
        Refresh();
    }

    public void Refresh()
    {
        var catalog = AppServices.ModCache.AllMods;

        Authors.Clear();
        foreach (var followed in AppServices.Followed.All)
        {
            // The name the listings give now, falling back to the one saved when they were followed.
            var mods = catalog.Where(m => NewModAnnouncer.IsBy(m, followed.Id)).ToList();
            var name = mods.SelectMany(NewModAnnouncer.AuthorsOf).FirstOrDefault(o => o.Id == followed.Id)?.Name
                ?? followed.Name ?? followed.Id.ToString();
            Authors.Add(new FollowedAuthorRow(followed.Id, name, mods.Count));
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
    private void Open(FollowedAuthorRow? row)
    {
        if (row is not null) AppNavigation.ShowAuthor(row.Id, row.Name);
    }

    [RelayCommand]
    private void Unfollow(FollowedAuthorRow? row)
    {
        if (row is not null) AppServices.Followed.Set(row.Id, row.Name, follow: false);
    }
}
