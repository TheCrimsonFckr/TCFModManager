namespace TCFModManagement.Core.Services;

// 
// An "identifier:version" pair, the format the sp-mod.com API uses to describe an installed mod or
// addon in the updates/dependencies endpoints.
// 
public readonly record struct IdentifierVersionPair(string Identifier, string Version)
{
    public override string ToString() => $"{Identifier}:{Version}";

    public static string Join(IEnumerable<IdentifierVersionPair> pairs) =>
        string.Join(",", pairs.Select(p => p.ToString()));

    // Parses a single "identifier:version" pair, splitting on the first colon.
    public static bool TryParse(string text, out IdentifierVersionPair pair)
    {
        var separatorIndex = text.IndexOf(':');
        if (separatorIndex <= 0 || separatorIndex == text.Length - 1)
        {
            pair = default;
            return false;
        }

        pair = new IdentifierVersionPair(
            text[..separatorIndex].Trim(),
            text[(separatorIndex + 1)..].Trim());
        return true;
    }
}
