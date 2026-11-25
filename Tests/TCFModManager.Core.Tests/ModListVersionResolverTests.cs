using System.Net;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using TCFModManager.Core.SpModApi;
using Xunit;

namespace TCFModManager.Core.Tests;

public class ModListVersionResolverTests
{
    private const string Spt = "4.1.3";

    private sealed class FakeApi : IModListVersionApi
    {
        public Dictionary<int, IReadOnlyList<ModVersion>> Mods { get; } = [];
        public Dictionary<int, IReadOnlyList<AddonVersion>> Addons { get; } = [];
        public Dictionary<int, IReadOnlyList<ModVersion>> ModHistory { get; } = [];
        public Dictionary<int, IReadOnlyList<AddonVersion>> AddonHistory { get; } = [];

        public Exception? ModsThrow { get; set; }

        public List<string> Calls { get; } = [];

        public Task<IReadOnlyDictionary<int, IReadOnlyList<ModVersion>>> RecentModVersionsAsync(IReadOnlyList<int> modIds, CancellationToken ct)
        {
            Calls.Add($"mods {string.Join(",", modIds)}");
            if (ModsThrow is not null) throw ModsThrow;
            return Task.FromResult<IReadOnlyDictionary<int, IReadOnlyList<ModVersion>>>(
                Mods.Where(kv => modIds.Contains(kv.Key)).ToDictionary());
        }

        public Task<IReadOnlyDictionary<int, IReadOnlyList<AddonVersion>>> RecentAddonVersionsAsync(IReadOnlyList<int> addonIds, CancellationToken ct)
        {
            Calls.Add($"addons {string.Join(",", addonIds)}");
            return Task.FromResult<IReadOnlyDictionary<int, IReadOnlyList<AddonVersion>>>(
                Addons.Where(kv => addonIds.Contains(kv.Key)).ToDictionary());
        }

        public Task<IReadOnlyList<ModVersion>> MatchingModVersionsAsync(int modId, string version, CancellationToken ct)
        {
            Calls.Add($"match-mod {modId} {version}");
            return Task.FromResult<IReadOnlyList<ModVersion>>(
                [.. ModHistory.GetValueOrDefault(modId, []).Where(v => v.Version == version)]);
        }

        public Task<IReadOnlyList<AddonVersion>> MatchingAddonVersionsAsync(int addonId, string version, CancellationToken ct)
        {
            Calls.Add($"match-addon {addonId} {version}");
            return Task.FromResult<IReadOnlyList<AddonVersion>>(
                [.. AddonHistory.GetValueOrDefault(addonId, []).Where(v => v.Version == version)]);
        }

        public Task<IReadOnlyList<ModVersion>> NewestModVersionsAsync(int modId, CancellationToken ct)
        {
            Calls.Add($"newest-mod {modId}");
            return Task.FromResult(ModHistory.GetValueOrDefault(modId, []));
        }

        public Task<IReadOnlyList<AddonVersion>> NewestAddonVersionsAsync(int addonId, CancellationToken ct)
        {
            Calls.Add($"newest-addon {addonId}");
            return Task.FromResult(AddonHistory.GetValueOrDefault(addonId, []));
        }
    }

    private static ModVersion Mv(int id, string version, string spt = "~4.1") =>
        new() { Id = id, Version = version, SptVersionConstraint = spt, Link = $"https://sp-mod.com/mod/download/{id}" };

    private static AddonVersion Av(int id, string version, string parent) =>
        new() { Id = id, Version = version, ModVersionConstraint = parent, Link = $"https://sp-mod.com/addon/download/{id}" };

    private static ModListAction Fetch(int id, string? version, bool isAddon = false, int? versionId = null, bool fitOverride = false) => new()
    {
        Kind = ModListActionKind.Install,
        Name = $"{(isAddon ? "Addon" : "Mod")} {id}",
        ModId = id,
        IsAddon = isAddon,
        VersionId = versionId,
        TargetVersion = version,
        Entry = new ModListEntry { Name = $"Entry {id}", ModId = id, IsAddon = isAddon, Version = version, FitOverride = fitOverride },
    };

    private static ModListVersionContext Context(
        IEnumerable<int> mods,
        IEnumerable<(int Id, int Parent)>? addons = null,
        IEnumerable<ModListCandidate>? installed = null,
        IEnumerable<ModListEntry>? entries = null) => new()
    {
        Installed = [.. installed ?? []],
        ListEntries = [.. entries ?? []],
        Mods = mods.ToDictionary(id => id, id => new Mod { Id = id, Name = $"Mod {id}" }),
        Addons = (addons ?? []).ToDictionary(a => a.Id, a => new Addon { Id = a.Id, ModId = a.Parent, Name = $"Addon {a.Id}" }),
        SptVersion = Spt,
    };

    private static Task<ModListVersionResolution> Resolve(FakeApi api, ModListVersionContext context, params ModListAction[] fetches) =>
        ModListVersionResolver.ResolveAsync(fetches, context, api);

    [Fact]
    public async Task Pinned_versions_in_the_batch_answer_cost_one_request()
    {
        var api = new FakeApi();
        api.Mods[1] = [Mv(11, "1.0.0")];
        api.Mods[2] = [Mv(21, "2.0.0")];
        api.Mods[3] = [Mv(31, "3.0.0")];

        var result = await Resolve(api, Context([1, 2, 3]), Fetch(1, "1.0.0"), Fetch(2, "2.0.0"), Fetch(3, "3.0.0"));

        Assert.Equal(["mods 1,2,3"], api.Calls);
        Assert.Equal(1, result.Requests);
        Assert.Equal([11, 21, 31], result.Ready.Select(r => r.Version.Id));
        Assert.All(result.Ready, r => Assert.NotNull(r.Version.Link));
    }

    [Fact]
    public async Task A_long_list_is_read_fifty_mods_a_request()
    {
        var api = new FakeApi();
        var ids = Enumerable.Range(1, 120).ToList();
        foreach (var id in ids) api.Mods[id] = [Mv(id * 10, "1.0.0")];

        var result = await Resolve(api, Context(ids), [.. ids.Select(id => Fetch(id, "1.0.0"))]);

        Assert.Equal(3, result.Requests);
        Assert.Equal(120, result.Ready.Count);
    }

    [Fact]
    public async Task The_version_id_wins_over_the_version_string()
    {
        var api = new FakeApi();
        api.Mods[1] = [Mv(12, "1.0.0"), Mv(11, "1.0.0")];

        var result = await Resolve(api, Context([1]), Fetch(1, "1.0.0", versionId: 11));

        Assert.Equal(11, Assert.Single(result.Ready).Version.Id);
    }

    [Fact]
    public async Task A_gone_pin_below_the_embed_cap_is_offered_the_newest_without_asking_again()
    {
        var api = new FakeApi();
        api.Mods[1] = [Mv(12, "2.0.0"), Mv(11, "1.9.0")];
        api.Mods[2] = [Mv(23, "3.0.0"), Mv(22, "2.0.0"), Mv(21, "1.0.0")];

        var result = await Resolve(api, Context([1, 2]), Fetch(1, "1.0.0"), Fetch(2, "3.0.0"));

        Assert.Equal(["mods 1,2"], api.Calls);
        var offer = Assert.Single(result.Offers);
        Assert.Equal("1.0.0", offer.Wanted);
        Assert.Equal("2.0.0", offer.Available.Version);
        Assert.False(offer.DoesNotFitParent);
    }

    [Fact]
    public async Task A_pin_older_than_the_embed_at_the_cap_is_asked_about_on_its_own()
    {
        var api = new FakeApi();
        api.Mods[1] = [Mv(14, "4.0.0"), Mv(13, "3.0.0"), Mv(12, "2.0.0")];
        api.ModHistory[1] = [Mv(11, "1.0.0")];

        var result = await Resolve(api, Context([1]), Fetch(1, "1.0.0"));

        Assert.Equal(["mods 1", "match-mod 1 1.0.0"], api.Calls);
        Assert.Equal(11, Assert.Single(result.Ready).Version.Id);
        Assert.Equal(2, result.Requests);
    }

    [Fact]
    public async Task A_gone_pin_with_nothing_to_offer_is_unavailable()
    {
        var api = new FakeApi();
        api.Mods[1] = [Mv(12, "2.0.0", "~4.2")];
        api.Mods[2] = [Mv(22, "2.0.0"), Mv(21, "1.0.0")];

        var result = await Resolve(api, Context([1, 2]), Fetch(1, "1.0.0"), Fetch(2, "1.0.0"));

        var gone = Assert.Single(result.Unavailable);
        Assert.Equal(ModListUnavailableReason.VersionGone, gone.Reason);
        Assert.Equal("1.0.0", gone.Version);
        Assert.Equal(["mods 1,2"], api.Calls);
    }

    [Fact]
    public async Task No_version_named_takes_the_newest_that_fits_this_spt()
    {
        var api = new FakeApi();
        api.Mods[1] = [Mv(13, "3.0.0", "~4.2"), Mv(12, "2.0.0", "~4.1"), Mv(11, "1.0.0", "~4.0")];

        var result = await Resolve(api, Context([1]), Fetch(1, null));

        Assert.Equal("2.0.0", Assert.Single(result.Ready).Version.Version);
        Assert.Equal(["mods 1"], api.Calls);
    }

    [Fact]
    public async Task Nothing_fitting_at_the_cap_reads_further_back()
    {
        var api = new FakeApi();
        api.Mods[1] = [Mv(13, "3.0.0", "~4.2"), Mv(12, "2.0.0", "~4.2")];
        api.ModHistory[1] = [Mv(13, "3.0.0", "~4.2"), Mv(12, "2.0.0", "~4.2"), Mv(11, "1.0.0", "~4.1")];

        var result = await Resolve(api, Context([1]), Fetch(1, null));

        Assert.Equal(["mods 1", "newest-mod 1"], api.Calls);
        Assert.Equal("1.0.0", Assert.Single(result.Ready).Version.Version);
    }

    [Fact]
    public async Task Nothing_fitting_below_the_cap_says_so_without_asking_again()
    {
        var api = new FakeApi();
        api.Mods[1] = [Mv(12, "2.0.0", "~4.2")];
        api.Mods[2] = [Mv(22, "2.0.0"), Mv(21, "1.0.0")];

        var result = await Resolve(api, Context([1, 2]), Fetch(1, null), Fetch(2, "1.0.0"));

        var none = Assert.Single(result.Unavailable);
        Assert.Equal(ModListUnavailableReason.NoVersionForSpt, none.Reason);
        Assert.Equal(Spt, none.Version);
        Assert.Equal(["mods 1,2"], api.Calls);
    }

    [Fact]
    public async Task Nothing_published_at_all()
    {
        var api = new FakeApi();
        api.Mods[1] = [];
        api.Mods[2] = [Mv(21, "1.0.0")];

        var result = await Resolve(api, Context([1, 2]), Fetch(1, null), Fetch(2, "1.0.0"));

        Assert.Equal(ModListUnavailableReason.NoVersions, Assert.Single(result.Unavailable).Reason);
    }

    [Fact]
    public async Task A_mod_missing_from_the_batch_answer_is_asked_about_on_its_own()
    {
        var api = new FakeApi();
        api.Mods[1] = [Mv(11, "1.0.0")];
        api.ModHistory[2] = [Mv(21, "1.0.0")];

        var result = await Resolve(api, Context([1, 2]), Fetch(1, "1.0.0"), Fetch(2, "1.0.0"));

        Assert.Equal(["mods 1,2", "match-mod 2 1.0.0"], api.Calls);
        Assert.Equal([11, 21], result.Ready.Select(r => r.Version.Id));
    }

    [Fact]
    public async Task A_failed_batch_fails_its_entries_and_the_rest_carry_on()
    {
        var api = new FakeApi { ModsThrow = new SpModApiException(HttpStatusCode.ServiceUnavailable, null, "sp-mod is down") };
        api.Addons[20] = [Av(201, "1.0.0", "^1.0.0")];
        var installed = new ModListCandidate { Name = "Parent", ModId = 10, Version = "1.0.0" };

        var result = await Resolve(
            api,
            Context([1, 10], addons: [(20, 10)], installed: [installed]),
            Fetch(1, "1.0.0"),
            Fetch(20, "1.0.0", isAddon: true));

        var failed = Assert.Single(result.Unavailable);
        Assert.Equal(ModListUnavailableReason.SpModFailed, failed.Reason);
        Assert.Equal("sp-mod is down", failed.Detail);
        Assert.Equal(201, Assert.Single(result.Ready).Version.Id);
    }

    [Fact]
    public async Task Cancelling_is_not_reported_as_a_failure()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var api = new FakeApi { ModsThrow = new TaskCanceledException() };

        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            ModListVersionResolver.ResolveAsync([Fetch(1, "1.0.0")], Context([1]), api, ct: cts.Token));
    }

    [Fact]
    public async Task An_addon_pin_its_parent_wont_take_is_offered_the_newest_that_fits()
    {
        var api = new FakeApi();
        api.Addons[20] = [Av(202, "1.1.0", "^2.0.0"), Av(201, "1.0.0", "^1.0.0")];
        var installed = new ModListCandidate { Name = "Parent", ModId = 10, Version = "2.0.0" };

        var result = await Resolve(api, Context([10], addons: [(20, 10)], installed: [installed]), Fetch(20, "1.0.0", isAddon: true));

        var offer = Assert.Single(result.Offers);
        Assert.True(offer.DoesNotFitParent);
        Assert.Equal("2.0.0", offer.ParentVersion);
        Assert.Equal("Mod 10", offer.ParentName);
        Assert.Equal("1.1.0", offer.Available.Version);
        Assert.Equal(["addons 20"], api.Calls);
    }

    [Fact]
    public async Task An_addon_installed_anyway_keeps_its_pin()
    {
        var api = new FakeApi();
        api.Addons[20] = [Av(202, "1.1.0", "^2.0.0"), Av(201, "1.0.0", "^1.0.0")];
        var installed = new ModListCandidate { Name = "Parent", ModId = 10, Version = "2.0.0" };

        var result = await Resolve(
            api, Context([10], addons: [(20, 10)], installed: [installed]), Fetch(20, "1.0.0", isAddon: true, fitOverride: true));

        var ready = Assert.Single(result.Ready);
        Assert.Equal(201, ready.Version.Id);
        Assert.Equal("2.0.0", ready.Target.FitOverride);
    }

    [Fact]
    public async Task An_addon_follows_the_version_this_apply_fetches_for_its_parent()
    {
        var api = new FakeApi();
        api.Mods[10] = [Mv(102, "2.0.0")];
        api.Addons[20] = [Av(202, "1.1.0", "^2.0.0"), Av(201, "1.0.0", "^1.0.0")];
        var installed = new ModListCandidate { Name = "Parent", ModId = 10, Version = "1.0.0" };

        var result = await Resolve(
            api, Context([10], addons: [(20, 10)], installed: [installed]), Fetch(10, "2.0.0"), Fetch(20, null, isAddon: true));

        Assert.Equal(["2.0.0", "1.1.0"], result.Ready.Select(r => r.Version.Version));
    }

    [Fact]
    public async Task An_addon_with_no_parent_anywhere_is_refused_without_asking()
    {
        var api = new FakeApi();

        var result = await Resolve(api, Context([10], addons: [(20, 10)]), Fetch(20, "1.0.0", isAddon: true));

        var orphan = Assert.Single(result.Unavailable);
        Assert.Equal(ModListUnavailableReason.OrphanedAddon, orphan.Reason);
        Assert.Equal(10, orphan.ParentId);
        Assert.Equal("Mod 10", orphan.ParentName);
        Assert.Empty(api.Calls);
        Assert.Equal(0, result.Requests);
    }

    [Fact]
    public async Task An_addon_whose_parent_the_list_places_elsewhere_still_resolves()
    {
        var api = new FakeApi();
        api.Addons[20] = [Av(201, "1.0.0", "^1.0.0")];
        var listed = new ModListEntry { Name = "Parent", ModId = 10, Version = "1.0.0" };

        var result = await Resolve(api, Context([10], addons: [(20, 10)], entries: [listed]), Fetch(20, "1.0.0", isAddon: true));

        Assert.Equal(201, Assert.Single(result.Ready).Version.Id);
    }

    [Fact]
    public async Task No_listing_and_no_catalog_entry_are_refused_without_asking()
    {
        var api = new FakeApi();
        var noListing = new ModListAction { Kind = ModListActionKind.Install, Name = "Hand-made" };

        var result = await Resolve(api, Context([]), noListing, Fetch(7, "1.0.0"), Fetch(8, "1.0.0", isAddon: true));

        Assert.Equal(
            [ModListUnavailableReason.NoListing, ModListUnavailableReason.NoListing, ModListUnavailableReason.NoAddonListing],
            result.Unavailable.Select(u => u.Reason));
        Assert.Empty(api.Calls);
    }

    [Fact]
    public async Task Results_keep_the_order_of_the_fetches()
    {
        var api = new FakeApi();
        api.Mods[1] = [Mv(11, "1.0.0")];
        api.Mods[3] = [Mv(31, "1.0.0")];

        var result = await Resolve(api, Context([1, 3]), Fetch(3, "1.0.0"), Fetch(2, "1.0.0"), Fetch(1, "1.0.0"));

        Assert.Equal([31, 11], result.Ready.Select(r => r.Version.Id));
    }
}
