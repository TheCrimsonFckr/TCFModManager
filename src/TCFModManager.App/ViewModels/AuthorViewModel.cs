using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using TCFModManager.Core.SpModApi;

namespace TCFModManager.App.ViewModels;

// One of an author's mods on their page.
public sealed class AuthorModRow(Mod mod, bool isInstalled)
{
    public Mod Mod { get; } = mod;

    public string Name => Mod.Name ?? string.Empty;

    public string? Teaser => Mod.Teaser;

    public string? Thumbnail => Mod.Thumbnail;

    public bool IsInstalled { get; } = isInstalled;

    public string? Published => (Mod.PublishedAt ?? Mod.CreatedAt)?.ToLocalTime().ToString("d", CultureInfo.CurrentCulture);
}

// One of an author's addons on their page, with the mod it belongs to.
public sealed class AuthorAddonRow(Addon addon, string? parentName)
{
    public Addon Addon { get; } = addon;

    public string Name => Addon.Name ?? string.Empty;

    public string? Thumbnail => Addon.Thumbnail;

    public string? ForMod => parentName is null ? null : LocalizationService.Text(Strings.Author_AddonForFormat, parentName);
}

//
// OPEN-12 A4 (R22): an sp-mod author's page - opened from any author name, Back returns. Built from
// the cached catalog and addon list alone, newest first: no profile page is read, so there is no
// follower count or bio, only what the listings carry (name, avatar, cover).
//
public sealed partial class AuthorViewModel : LocalizedViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFollowing), nameof(FollowLabel), nameof(FollowGlyph))]
    private int _authorId;

    [ObservableProperty]
    private string? _name;

    [ObservableProperty]
    private string? _avatarUrl;

    [ObservableProperty]
    private string? _coverUrl;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _statusMessage;

    public ObservableCollection<AuthorModRow> Mods { get; } = [];

    public ObservableCollection<AuthorAddonRow> Addons { get; } = [];

    public bool HasAddons => Addons.Count > 0;

    public string CountsLabel => LocalizationService.Text(Strings.Author_CountsFormat, Mods.Count, Addons.Count);

    public bool IsFollowing => AppServices.Followed.IsFollowing(AuthorId);

    public string FollowLabel => IsFollowing ? Strings.Author_Unfollow : Strings.Author_Follow;

    public string FollowGlyph => IsFollowing ? "PersonDelete24" : "PersonAdd24";

    private string? _siteBase;

    public AuthorViewModel()
    {
        AppServices.Followed.Changed += (_, _) => RaiseFollow();
    }

    private void RaiseFollow()
    {
        OnPropertyChanged(nameof(IsFollowing));
        OnPropertyChanged(nameof(FollowLabel));
        OnPropertyChanged(nameof(FollowGlyph));
    }

    protected internal override void RefreshText()
    {
        base.RefreshText();
        RaiseFollow();
        OnPropertyChanged(nameof(CountsLabel));
    }

    public async Task LoadAsync(int authorId, string? name)
    {
        AuthorId = authorId;
        Name = name;
        AvatarUrl = CoverUrl = null;
        StatusMessage = null;
        Mods.Clear();
        Addons.Clear();
        IsLoading = true;

        try
        {
            await AppServices.ModCache.EnsureLoadedAsync();
            await AppServices.Addons.EnsureLoadedAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or SpModApiException)
        {
            AppLog.Warn("Authors", $"couldn't load the catalog for author {authorId}: {ex.Message}");
            StatusMessage = Strings.Author_CatalogFailed;
        }

        if (AuthorId != authorId) return;

        var mods = AppServices.ModCache.AllMods
            .Where(m => NewModAnnouncer.IsBy(m, authorId))
            .OrderByDescending(m => m.PublishedAt ?? m.CreatedAt ?? DateTimeOffset.MinValue)
            .ToList();

        var addons = AppServices.Addons.AllAddons
            .Where(a => NewModAnnouncer.IsBy(a, authorId))
            .OrderByDescending(a => a.PublishedAt ?? a.CreatedAt ?? DateTimeOffset.MinValue)
            .ToList();

        // The author as the listings describe them - the owner entry carries the pictures.
        var owner = mods.SelectMany(NewModAnnouncer.AuthorsOf).FirstOrDefault(o => o.Id == authorId)
            ?? addons.Select(a => a.Owner).FirstOrDefault(o => o?.Id == authorId);
        if (owner is not null)
        {
            Name = owner.Name ?? Name;
            AvatarUrl = owner.ProfilePhotoUrl;
            CoverUrl = owner.CoverPhotoUrl;
        }

        _siteBase = mods.Select(m => m.DetailUrl).Concat(addons.Select(a => a.DetailUrl))
            .Select(u => Uri.TryCreate(u, UriKind.Absolute, out var uri) ? uri.GetLeftPart(UriPartial.Authority) : null)
            .FirstOrDefault(u => u is not null);

        var installedIds = AppServices.InstallManifest.Load().Mods.Where(r => !r.IsAddon).Select(r => r.ModId).ToHashSet();
        foreach (var mod in mods) Mods.Add(new AuthorModRow(mod, installedIds.Contains(mod.Id) || AppServices.Browse.IsInstalled(mod)));

        var parents = AppServices.ModCache.AllMods.ToDictionary(m => m.Id, m => m.Name);
        foreach (var addon in addons)
            Addons.Add(new AuthorAddonRow(addon, addon.ModId is { } parent ? parents.GetValueOrDefault(parent) : null));

        OnPropertyChanged(nameof(HasAddons));
        OnPropertyChanged(nameof(CountsLabel));
        RaiseFollow();
        IsLoading = false;

        if (Mods.Count == 0 && Addons.Count == 0 && StatusMessage is null) StatusMessage = Strings.Author_NothingListed;
    }

    [RelayCommand]
    private void ToggleFollow() => AppServices.Followed.Toggle(AuthorId, Name);

    // sp-mod's own page for the author, on the host their listings live on.
    private const string DefaultSite = "https://sp-mod.com";

    [RelayCommand]
    private void OpenOnSite()
    {
        var site = _siteBase ?? DefaultSite;
        MarkupActions.OpenInBrowser($"{site}/user/{AuthorId}/u");
    }

    [RelayCommand]
    private async Task OpenModAsync(AuthorModRow? row)
    {
        if (row is not null) await AppServices.Browse.LoadDetailsAsync(row.Mod);
    }

    [RelayCommand]
    private void OpenAddon(AuthorAddonRow? row)
    {
        if (row?.Addon.DetailUrl is { } url) MarkupActions.OpenInBrowser(url);
    }
}
