using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

public enum SpModListItemKind
{
    Mod,
    Addon,
}

//
// One row of an sp-mod list, as the page shows it.
//
// Version is what the page displays, not something the list's author chose: sp-mod resolves each
// mod to its newest version compatible with the list's target SPT (or the newest outright when the
// list has none), and each addon to its newest version. NotCompatible marks a mod that has no
// version for the target, where the page shows the closest one instead.
//
// ParentModId is set on an addon when the page groups it under a mod. ParentOnList is false when
// that card is a "detached" one - sp-mod draws an addon whose parent isn't on the list under its
// parent anyway, and that parent is not a list entry.
//
public sealed record SpModListItem(
    SpModListItemKind Kind,
    int Id,
    string Name,
    string? Version,
    int? ParentModId = null,
    bool ParentOnList = true,
    bool NotCompatible = false);

public enum SpModListReadPass
{
    // Read off the page's group cards. Knows each addon's parent.
    Structural,

    // Read off every mod and addon link on the page, because the cards came up short.
    Fallback,
}

public enum SpModListNoticeKind
{
    // No group cards at all on a page that has entries: sp-mod has changed its layout.
    LayoutChanged,

    // The header's count for a kind differs from what was read.
    CountMismatch,

    // An entry with no readable version. Imported unpinned.
    MissingVersion,

    // An entry whose author has opted out of mod lists. sp-mod shows a placeholder, and it is not
    // counted in the header, so nothing can be imported for it.
    OptedOut,

    // An entry sp-mod says is no longer available - the mod or addon was removed or disabled. Unlike
    // OptedOut it IS counted in the header, so the count check allows for it.
    Unavailable,

    // The "by <author> · <date> · N mods" line wasn't found, so the counts can't be checked.
    NoHeader,
}

public sealed record SpModListNotice(
    SpModListNoticeKind Kind,
    SpModListItemKind? ItemKind = null,
    string? Name = null,
    int Expected = 0,
    int Read = 0);

public sealed class SpModListPage
{
    public required int ListId { get; init; }

    // The address it was read from, query string (share token) included.
    public required Uri Source { get; init; }

    // Null when neither the page title nor the heading could be read.
    public string? Name { get; init; }

    public string? Author { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }

    // The list's own target SPT version, from the header badge. Null when the list has none.
    public string? SptVersion { get; init; }

    public int? ExpectedMods { get; init; }

    public int? ExpectedAddons { get; init; }

    public required SpModListReadPass Pass { get; init; }

    public required IReadOnlyList<SpModListItem> Items { get; init; }

    public required IReadOnlyList<SpModListNotice> Notices { get; init; }

    public IEnumerable<SpModListItem> Mods => Items.Where(i => i.Kind == SpModListItemKind.Mod);

    public IEnumerable<SpModListItem> Addons => Items.Where(i => i.Kind == SpModListItemKind.Addon);

    //
    // The list ready to store. Entries keep page order, then anything the user added beyond the
    // page. The Id is derived from the list id, so importing the same sp-mod list again replaces the
    // stored one rather than adding a second.
    //
    // choices carries what was decided in the import window - or, on a refresh, what was decided
    // last time (SpModListChoices.From). Without it, every page item goes in.
    //
    public ModList ToModList(string fallbackName, DateTimeOffset now, SpModListChoices? choices = null)
    {
        choices ??= SpModListChoices.None;

        var excluded = new HashSet<string>(choices.Excluded, StringComparer.OrdinalIgnoreCase);

        var fromPage = Items
            .Where(i => !excluded.Contains(SpModListSource.RefFor(i.Kind == SpModListItemKind.Addon, i.Id)))
            .Select(i => new ModListEntry
            {
                Name = i.Name,
                ModId = i.Id,
                IsAddon = i.Kind == SpModListItemKind.Addon,
                Version = i.Version,
            })
            .ToList();

        var onPage = new HashSet<string>(fromPage.Select(e => SpModListSource.RefFor(e)!), StringComparer.OrdinalIgnoreCase);

        var added = choices.Added
            .Where(e => SpModListSource.RefFor(e) is { } r && !onPage.Contains(r) && !excluded.Contains(r))
            .GroupBy(e => SpModListSource.RefFor(e)!, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        var list = new ModList
        {
            Id = SpModListImport.ListIdFor(ListId),
            Name = string.IsNullOrWhiteSpace(Name) ? fallbackName : Name,
            Revision = 1,
            Origin = ModListOrigin.Imported,
            Policy = ModListPolicy.Additive,
            Source = Author,
            SptVersion = choices.SptVersion ?? SptVersion,
            CreatedAt = now,
            UpdatedAt = UpdatedAt ?? now,
            SpModSource = new SpModListSource
            {
                ListId = ListId,
                Url = Source.ToString(),
                ReadAt = now,
                PageUpdatedAt = UpdatedAt,
                Retargeted = choices.Retargeted,
                DependenciesAdded = choices.DependenciesAdded,
                Excluded = [.. excluded.Order(StringComparer.Ordinal)],
                Added = [.. added.Select(e => SpModListSource.RefFor(e)!)],
            },
        };

        list.Entries.AddRange(fromPage);
        list.Entries.AddRange(added);

        return list;
    }
}

//
// What the import window decided, carried into the stored list.
//
// SptVersion is the list's target after a retarget - this install's SPT - and null to keep the
// page's own. Added holds whole entries, because a missing parent or a dependency is not on the
// page and has to bring its own name and version.
//
public sealed record SpModListChoices(
    IReadOnlyCollection<string> Excluded,
    IReadOnlyList<ModListEntry> Added,
    bool Retargeted = false,
    bool DependenciesAdded = false,
    string? SptVersion = null)
{
    public static SpModListChoices None { get; } = new([], []);

    // The choices a stored list was made with, for reading its page again.
    public static SpModListChoices From(ModList stored)
    {
        if (stored.SpModSource is not { } source) return None;

        var added = new HashSet<string>(source.Added, StringComparer.OrdinalIgnoreCase);

        return new SpModListChoices(
            source.Excluded,
            [.. stored.Entries.Where(e => SpModListSource.RefFor(e) is { } r && added.Contains(r))],
            source.Retargeted,
            source.DependenciesAdded,
            source.Retargeted ? stored.SptVersion : null);
    }
}

public sealed record SpModListVersionChange(ModListEntry From, ModListEntry To);

// What reading a list again changed, entry by entry. Matched on mod/addon id.
public sealed record SpModListDiff(
    IReadOnlyList<ModListEntry> Added,
    IReadOnlyList<ModListEntry> Removed,
    IReadOnlyList<SpModListVersionChange> VersionChanged,
    string? OldName,
    string? OldSptVersion)
{
    public bool NameChanged => OldName is not null;

    public bool SptVersionChanged { get; init; }

    public bool HasChanges => Added.Count > 0 || Removed.Count > 0 || VersionChanged.Count > 0 || NameChanged || SptVersionChanged;
}

//
// The stored list after an import or refresh, and what changed.
//
// Previous is the list that was stored before, null for a first import. Diff is null then too.
//
public sealed record SpModListUpdate(ModList List, ModList? Previous, SpModListDiff? Diff)
{
    public bool IsNew => Previous is null;

    public bool HasChanges => Diff?.HasChanges ?? true;
}

public enum SpModListFailure
{
    // Not an sp-mod list address. Nothing was requested.
    NotAListUrl,

    // The request never got an answer: offline, DNS, TLS, timeout.
    Network,

    // sp-mod answered with an error status (404 for a missing or private list).
    HttpStatus,

    // A bot check or challenge page came back instead of the list.
    Blocked,

    // A page came back but it isn't a list page.
    NotAListPage,
}

// A read: the page, or why there isn't one. Never throws at the caller.
public sealed record SpModListRead(
    SpModListPage? Page,
    SpModListFailure? Failure = null,
    HttpStatusCode? Status = null,
    string? Detail = null)
{
    public bool Succeeded => Page is not null;

    public static SpModListRead Failed(SpModListFailure failure, HttpStatusCode? status = null, string? detail = null) =>
        new(null, failure, status, detail);
}

//
// Reads a public (or share-linked) sp-mod Mod List page into a list the app can store.
//
// sp-mod's API has no list endpoint, so this reads the server-rendered page, one request per
// import, made only when the user asks. All of the page knowledge lives in Parse.
//
public static partial class SpModListImport
{
    private const string Area = "SpModListImport";

    private const string Host = "sp-mod.com";

    // A fixed namespace for the v5 ids below. Changing it would orphan every imported list.
    private static readonly Guid IdNamespace = new("6f1c8e3a-2b7d-5c49-9a0e-4d3b1f8a7c26");

    [GeneratedRegex(@"^/list/(\d{1,9})(?:/[^/?#]*)?/?$", RegexOptions.CultureInvariant)]
    private static partial Regex ListPath();

    [GeneratedRegex(@"/(mod|addon)/(\d{1,9})(?:[/?#]|$)", RegexOptions.CultureInvariant)]
    private static partial Regex ItemHref();

    [GeneratedRegex(@"(\d[\d,]*)\s+(mods?|addons?)\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex HeaderCount();

    [GeneratedRegex(@"\d+(?:\.\d+)+", RegexOptions.CultureInvariant)]
    private static partial Regex SptNumber();

    [GeneratedRegex(@"\s+-\s+Mod List\s+-\s+.*$", RegexOptions.CultureInvariant)]
    private static partial Regex TitleSuffix();

    [GeneratedRegex(@"^list-group-(?:(\d+)|detached-\d+)$", RegexOptions.CultureInvariant)]
    private static partial Regex GroupKey();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();

    //
    // Accepts https://sp-mod.com/list/<id>[/<slug>][?query], with or without the scheme or www,
    // surrounding whitespace allowed. The query is kept because a private list's share link
    // carries its token there.
    //
    public static bool TryParseListUrl(string? text, out int listId, out Uri url)
    {
        listId = 0;
        url = null!;

        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return false;

        if (!trimmed.Contains("://", StringComparison.Ordinal)) trimmed = "https://" + trimmed;

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed)) return false;
        if (parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeHttp) return false;

        var host = parsed.Host.ToLowerInvariant();
        if (host != Host && host != "www." + Host) return false;

        var match = ListPath().Match(parsed.AbsolutePath);
        if (!match.Success) return false;

        listId = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        if (listId <= 0) return false;

        url = CanonicalUrl(listId, parsed);
        return true;
    }

    internal static Uri CanonicalUrl(int listId, Uri source)
    {
        var slug = source.AbsolutePath.TrimEnd('/').Split('/').Skip(3).FirstOrDefault();
        var path = string.IsNullOrEmpty(slug) ? $"/list/{listId}" : $"/list/{listId}/{slug}";

        return new UriBuilder(Uri.UriSchemeHttps, Host) { Path = path, Query = source.Query.TrimStart('?') }.Uri;
    }

    // RFC 4122 version 5 (SHA-1, name-based) over "sp-mod.com/list/<id>".
    public static Guid ListIdFor(int listId)
    {
        var name = Encoding.UTF8.GetBytes($"{Host}/list/{listId.ToString(CultureInfo.InvariantCulture)}");
        var ns = IdNamespace.ToByteArray(bigEndian: true);

        var hash = SHA1.HashData([.. ns, .. name]);

        var bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

        return new Guid(bytes, bigEndian: true);
    }

    public static async Task<SpModListRead> ReadAsync(string text, HttpClient http, string userAgent, CancellationToken ct = default)
    {
        if (!TryParseListUrl(text, out _, out var url)) return SpModListRead.Failed(SpModListFailure.NotAListUrl);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd(userAgent);
        request.Headers.Accept.ParseAdd("text/html");
        request.Headers.AcceptLanguage.ParseAdd("en");
        request.Headers.Referrer = new Uri($"https://{Host}/");

        HttpResponseMessage response;
        string body;

        try
        {
            response = await http.SendAsync(request, ct).ConfigureAwait(false);
            body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            AppLog.Warn(Area, $"{url} - no response: {ex.Message}");
            return SpModListRead.Failed(SpModListFailure.Network, detail: ex.Message);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            AppLog.Warn(Area, $"{url} - timed out");
            return SpModListRead.Failed(SpModListFailure.Network, detail: ex.Message);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var blocked = LooksLikeChallenge(body);
                AppLog.Warn(Area, $"{url} - HTTP {(int)response.StatusCode}{(blocked ? " (challenge page)" : "")}");

                return blocked
                    ? SpModListRead.Failed(SpModListFailure.Blocked, response.StatusCode)
                    : SpModListRead.Failed(SpModListFailure.HttpStatus, response.StatusCode);
            }
        }

        var read = Parse(body, url);

        if (read.Page is { } page)
        {
            AppLog.Info(Area,
                $"{url} - {page.Pass}: {page.Mods.Count()}/{page.ExpectedMods?.ToString() ?? "?"} mods, " +
                $"{page.Addons.Count()}/{page.ExpectedAddons?.ToString() ?? "?"} addons, SPT {page.SptVersion ?? "none"}" +
                (page.Notices.Count == 0 ? "" : $", notices: {string.Join(", ", page.Notices.Select(n => n.Kind).Distinct())}"));
        }
        else
        {
            AppLog.Warn(Area, $"{url} - {read.Failure}");
        }

        return read;
    }

    //
    // The page, read. No network.
    //
    // Two passes. The structural one reads the group cards sp-mod renders for the list
    // (wire:key="list-group-<item>" holding a mod and its "list-addon-<item>" rows); it is the only
    // one that knows an addon's parent and which cards are detached. If it reads fewer entries
    // than the header says, the fallback reads every /mod/ and /addon/ link on the page and adds
    // what the first pass missed - the C tool's approach, which survives a redesign but can't tell
    // a detached parent from a real entry.
    //
    public static SpModListRead Parse(string html, Uri source)
    {
        if (!TryParseListUrl(source.ToString(), out var listId, out var url))
        {
            return SpModListRead.Failed(SpModListFailure.NotAListUrl);
        }

        if (string.IsNullOrWhiteSpace(html)) return SpModListRead.Failed(SpModListFailure.NotAListPage);

        var document = new HtmlParser().ParseDocument(html);

        var groups = document.QuerySelectorAll("[wire\\:key^='list-group-']").ToList();
        var header = FindHeader(document);
        var title = ReadName(document);

        if (groups.Count == 0 && header is null)
        {
            return LooksLikeChallenge(html)
                ? SpModListRead.Failed(SpModListFailure.Blocked)
                : SpModListRead.Failed(SpModListFailure.NotAListPage);
        }

        var notices = new List<SpModListNotice>();
        var items = new List<SpModListItem>();
        var seen = new HashSet<(SpModListItemKind, int)>();

        void Add(SpModListItem item)
        {
            if (seen.Add((item.Kind, item.Id))) items.Add(item);
        }

        foreach (var group in groups) ReadGroup(group, Add, notices);

        var (expectedMods, expectedAddons) = ReadCounts(header);
        if (header is null) notices.Add(new SpModListNotice(SpModListNoticeKind.NoHeader));

        // Unavailable entries are in the header's counts but can never be read.
        var unavailableMods = Unavailable(notices, SpModListItemKind.Mod);
        var unavailableAddons = Unavailable(notices, SpModListItemKind.Addon);

        var expectedTotal = (expectedMods ?? 0) - unavailableMods + (expectedAddons ?? 0) - unavailableAddons;
        var pass = SpModListReadPass.Structural;

        if (groups.Count == 0 && expectedTotal > 0) notices.Add(new SpModListNotice(SpModListNoticeKind.LayoutChanged));

        if (items.Count < expectedTotal)
        {
            pass = SpModListReadPass.Fallback;
            foreach (var item in ReadLinks(document)) Add(item);
        }

        foreach (var item in items.Where(i => i.Version is null))
        {
            notices.Add(new SpModListNotice(SpModListNoticeKind.MissingVersion, item.Kind, item.Name));
        }

        CheckCount(notices, SpModListItemKind.Mod, expectedMods - unavailableMods, items.Count(i => i.Kind == SpModListItemKind.Mod));
        CheckCount(notices, SpModListItemKind.Addon, expectedAddons - unavailableAddons, items.Count(i => i.Kind == SpModListItemKind.Addon));

        return new SpModListRead(new SpModListPage
        {
            ListId = listId,
            Source = url,
            Name = title,
            Author = header is null ? null : Text(header.QuerySelector("a[href*='/user/']")),
            UpdatedAt = ReadTime(header),
            SptVersion = ReadSptVersion(document),
            ExpectedMods = expectedMods,
            ExpectedAddons = expectedAddons,
            Pass = pass,
            Items = items,
            Notices = notices,
        });
    }

    private static void ReadGroup(IElement group, Action<SpModListItem> add, List<SpModListNotice> notices)
    {
        var key = GroupKey().Match(group.GetAttribute("wire:key") ?? "");
        var detached = key.Success && !key.Groups[1].Success;

        var addonRows = group.QuerySelectorAll("[wire\\:key^='list-addon-']").ToList();

        var modLink = group.QuerySelectorAll("a[href]")
            .FirstOrDefault(a => ItemKind(a) is (SpModListItemKind.Mod, _)
                                 && Text(a) is not null
                                 && !addonRows.Any(r => r.Contains(a)));

        int? parentId = null;

        if (modLink is not null && ItemKind(modLink) is (_, var modId))
        {
            parentId = modId;

            if (!detached)
            {
                add(new SpModListItem(
                    SpModListItemKind.Mod,
                    modId,
                    Text(modLink)!,
                    VersionAfter(modLink),
                    NotCompatible: IsMarkedNotCompatible(group, addonRows)));
            }
        }
        else if (!detached)
        {
            notices.Add(new SpModListNotice(PlaceholderKind(group, addonRows), SpModListItemKind.Mod));
        }

        foreach (var row in addonRows)
        {
            var link = row.QuerySelectorAll("a[href]")
                .FirstOrDefault(a => ItemKind(a) is (SpModListItemKind.Addon, _) && Text(a) is not null);

            if (link is null || ItemKind(link) is not (_, var addonId))
            {
                notices.Add(new SpModListNotice(PlaceholderKind(row, []), SpModListItemKind.Addon));
                continue;
            }

            add(new SpModListItem(
                SpModListItemKind.Addon,
                addonId,
                Text(link)!,
                VersionAfter(link),
                ParentModId: parentId,
                ParentOnList: !detached));
        }
    }

    //
    // A card or row with no link is one of two placeholders. An opted-out one carries a title line
    // (the captured name, or a generic one) in a .truncate element; an unavailable one is a single
    // italic sentence. Told apart by shape rather than by the English text.
    //
    private static SpModListNoticeKind PlaceholderKind(IElement element, List<IElement> exclude) =>
        element.QuerySelectorAll(".truncate").Any(t => !exclude.Any(r => r.Contains(t)))
            ? SpModListNoticeKind.OptedOut
            : SpModListNoticeKind.Unavailable;

    private static IEnumerable<SpModListItem> ReadLinks(IDocument document)
    {
        foreach (var a in document.QuerySelectorAll("a[href]"))
        {
            if (ItemKind(a) is not (var kind, var id)) continue;
            if (Text(a) is not { } name) continue;

            yield return new SpModListItem(kind, id, name, VersionAfter(a));
        }
    }

    private static (SpModListItemKind Kind, int Id)? ItemKind(IElement anchor)
    {
        var href = anchor.GetAttribute("href");
        if (string.IsNullOrEmpty(href)) return null;

        if (Uri.TryCreate(href, UriKind.Absolute, out var absolute))
        {
            var host = absolute.Host.ToLowerInvariant();
            if (host != Host && host != "www." + Host) return null;
            href = absolute.AbsolutePath;
        }

        var match = ItemHref().Match(href);
        if (!match.Success || match.Index != 0) return null;

        var kind = match.Groups[1].Value == "mod" ? SpModListItemKind.Mod : SpModListItemKind.Addon;
        return (kind, int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture));
    }

    // The version sits in the element right after the name link.
    private static string? VersionAfter(IElement anchor)
    {
        var next = anchor.NextElementSibling;
        if (next is null || next.LocalName != "span") return null;

        var text = Text(next);
        return text is null || !text.Any(char.IsDigit) ? null : text;
    }

    //
    // sp-mod's "Not compatible" badge is the only thing on a card that says so, and its text is
    // English. A miss costs nothing: retargeting checks every version against the install itself.
    //
    private static bool IsMarkedNotCompatible(IElement group, List<IElement> addonRows) =>
        group.QuerySelectorAll("[data-flux-badge]")
            .Any(b => !addonRows.Any(r => r.Contains(b))
                      && (Text(b)?.Contains("Not compatible", StringComparison.OrdinalIgnoreCase) ?? false));

    // The "by <author> · <time> · N mods · M addons" line: the first <p> with a user link and a time.
    private static IElement? FindHeader(IDocument document) =>
        document.QuerySelectorAll("p")
            .FirstOrDefault(p => p.QuerySelector("a[href*='/user/']") is not null && p.QuerySelector("time") is not null);

    private static (int? Mods, int? Addons) ReadCounts(IElement? header)
    {
        if (header is null) return (null, null);

        int? mods = 0;
        int? addons = 0;

        foreach (Match match in HeaderCount().Matches(header.TextContent))
        {
            var value = int.Parse(match.Groups[1].Value.Replace(",", ""), CultureInfo.InvariantCulture);

            if (match.Groups[2].Value.StartsWith("mod", StringComparison.OrdinalIgnoreCase)) mods = value;
            else addons = value;
        }

        // A list with nothing on it omits both counts, which reads the same as zero of each.
        return (mods, addons);
    }

    private static DateTimeOffset? ReadTime(IElement? header)
    {
        var raw = header?.QuerySelector("time")?.GetAttribute("datetime");

        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }

    //
    // The list's target SPT, from the badge in the header. The per-mod badges on the cards carry
    // the same class, so anything inside a group card is skipped.
    //
    private static string? ReadSptVersion(IDocument document)
    {
        foreach (var badge in document.QuerySelectorAll(".badge-version"))
        {
            if (badge.Closest("[wire\\:key^='list-group-']") is not null) continue;

            var match = SptNumber().Match(badge.TextContent);
            if (match.Success) return match.Value;
        }

        return null;
    }

    private static string? ReadName(IDocument document)
    {
        var title = document.Title is { } t ? TitleSuffix().Replace(Clean(t), "") : null;
        return string.IsNullOrWhiteSpace(title) || !document.Title!.Contains("Mod List", StringComparison.Ordinal)
            ? null
            : title;
    }

    private static int Unavailable(List<SpModListNotice> notices, SpModListItemKind kind) =>
        notices.Count(n => n.Kind == SpModListNoticeKind.Unavailable && n.ItemKind == kind);

    private static void CheckCount(List<SpModListNotice> notices, SpModListItemKind kind, int? expected, int read)
    {
        if (expected is { } e && e != read)
        {
            notices.Add(new SpModListNotice(SpModListNoticeKind.CountMismatch, kind, Expected: e, Read: read));
        }
    }

    //
    // Cloudflare's own interstitials, not its beacon: every sp-mod page carries a
    // /cdn-cgi/challenge-platform/ script, the normal list pages and the 404 page included, so that
    // marker alone means nothing.
    //
    private static bool LooksLikeChallenge(string? html) =>
        html is not null
        && (html.Contains("<title>Just a moment", StringComparison.OrdinalIgnoreCase)
            || html.Contains("<title>Attention Required", StringComparison.OrdinalIgnoreCase)
            || html.Contains("cf_chl_opt", StringComparison.Ordinal)
            || html.Contains("cf-chl-", StringComparison.Ordinal));

    private static string? Text(IElement? element)
    {
        if (element is null) return null;

        var text = Clean(element.TextContent);
        return text.Length == 0 ? null : text;
    }

    private static string Clean(string text) => Whitespace().Replace(text, " ").Trim();

    //
    // Folds a fresh read into what is stored.
    //
    // The revision moves by one when anything a user would care about changed - an entry added,
    // removed or at a different version, the list's name or its target SPT - and stays put when
    // nothing did, so "updated from revision 3 to 4" means something. The stored list keeps its
    // CreatedAt; the read time is updated either way.
    //
    // An entry whose only change is its display name is not a change: mods get renamed, and the
    // new name is carried without bumping anything.
    //
    public static SpModListUpdate Merge(ModList? stored, ModList incoming)
    {
        if (stored is null) return new SpModListUpdate(incoming, null, null);

        var diff = Diff(stored, incoming);

        var merged = new ModList
        {
            Id = incoming.Id,
            Name = incoming.Name,
            Description = incoming.Description,
            Revision = stored.Revision + (diff.HasChanges ? 1 : 0),
            Origin = incoming.Origin,
            Policy = incoming.Policy,
            DerivedFrom = incoming.DerivedFrom,
            Source = incoming.Source,
            SptVersion = incoming.SptVersion,
            CreatedAt = stored.CreatedAt,
            UpdatedAt = diff.HasChanges ? incoming.UpdatedAt : stored.UpdatedAt,
            Entries = [.. incoming.Entries],
            SpModSource = incoming.SpModSource,
        };

        return new SpModListUpdate(merged, stored, diff);
    }

    public static SpModListDiff Diff(ModList before, ModList after)
    {
        static Dictionary<string, ModListEntry> Keyed(ModList list) =>
            list.Entries
                .Select(e => (Ref: SpModListSource.RefFor(e), Entry: e))
                .Where(x => x.Ref is not null)
                .GroupBy(x => x.Ref!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Entry, StringComparer.OrdinalIgnoreCase);

        var old = Keyed(before);
        var now = Keyed(after);

        var added = after.Entries.Where(e => SpModListSource.RefFor(e) is { } r && !old.ContainsKey(r)).ToList();
        var removed = before.Entries.Where(e => SpModListSource.RefFor(e) is { } r && !now.ContainsKey(r)).ToList();

        var changed = after.Entries
            .Where(e => SpModListSource.RefFor(e) is { } r
                        && old.TryGetValue(r, out var was)
                        && !string.Equals(was.Version, e.Version, StringComparison.OrdinalIgnoreCase))
            .Select(e => new SpModListVersionChange(old[SpModListSource.RefFor(e)!], e))
            .ToList();

        return new SpModListDiff(
            added,
            removed,
            changed,
            string.Equals(before.Name, after.Name, StringComparison.Ordinal) ? null : before.Name,
            before.SptVersion)
        {
            SptVersionChanged = !string.Equals(before.SptVersion, after.SptVersion, StringComparison.OrdinalIgnoreCase),
        };
    }
}
