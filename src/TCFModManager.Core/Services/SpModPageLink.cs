using System.Globalization;
using System.Text.RegularExpressions;

namespace TCFModManager.Core.Services;

public enum SpModLinkKind
{
    Mod,
    Addon,
    List,
}

public readonly record struct SpModLink(SpModLinkKind Kind, int Id);

public static partial class SpModPageLink
{
    private const string Host = "sp-mod.com";

    [GeneratedRegex(@"^/(mod|addon)/(\d{1,9})(?:/[^/?#]*)?/?$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex PagePath();

    public static SpModLink? Parse(string? text)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Contains('\n')) return null;

        if (SpModListImport.TryParseListUrl(trimmed, out var listId, out _)) return new SpModLink(SpModLinkKind.List, listId);

        if (!trimmed.Contains("://", StringComparison.Ordinal)) trimmed = "https://" + trimmed;
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed)) return null;
        if (parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeHttp) return null;

        var host = parsed.Host.ToLowerInvariant();
        if (host != Host && host != "www." + Host) return null;

        var match = PagePath().Match(parsed.AbsolutePath);
        if (!match.Success) return null;

        var id = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        if (id <= 0) return null;

        var kind = string.Equals(match.Groups[1].Value, "addon", StringComparison.OrdinalIgnoreCase) ? SpModLinkKind.Addon : SpModLinkKind.Mod;
        return new SpModLink(kind, id);
    }
}
