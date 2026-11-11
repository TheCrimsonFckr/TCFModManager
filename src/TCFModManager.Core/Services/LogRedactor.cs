using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace TCFModManager.Core.Services;

//
// Takes out of log text what someone asking for help shouldn't have to share. SPT's
// logs hold full paths - so the Windows user name - and profile and account ids (seen in a real
// install's logs, 2026-10-03); a Fika host's logs hold players' addresses.
//
//   - the user's own folder becomes %USERPROFILE%, and the user name anywhere else becomes <user>;
//   - a 24-character id (SPT's MongoId: profiles, items, traders) becomes "id-" and six characters
//     of its SHA-256, the same each time, so lines about one id still read as one;
//   - an IPv4 address with a port becomes <ip>:<port>, except this machine's own (127.0.0.1, 0.0.0.0).
//     Without a port a dotted number is left alone - "1.8.0.0" is far more often a version.
//
// With RedactionValues, also:
//
//   - the computer's name becomes <machine>;
//   - the Server Map host, with or without a port, becomes <server> (the port kept);
//   - the Server Map shared key becomes <key> and the pinned thumbprint <thumbprint>.
//
public static partial class LogRedactor
{
    private const int MinimumNameLength = 3;
    private const int MinimumSecretLength = 6;

    [GeneratedRegex(@"(?<![0-9A-Fa-f])[0-9a-f]{24}(?![0-9A-Fa-f])")]
    private static partial Regex MongoId();

    [GeneratedRegex(@"(?<![\d.])(?<ip>\d{1,3}(?:\.\d{1,3}){3}):(?<port>\d{2,5})\b")]
    private static partial Regex Address();

    public static string Redact(string text, string? userProfile, string? userName) =>
        Redact(text, new RedactionValues { UserProfile = userProfile, UserName = userName });

    public static string Redact(string text, RedactionValues values)
    {
        if (string.IsNullOrEmpty(text)) return text;

        if (values.SharedKey is { Length: >= MinimumSecretLength } key)
            text = text.Replace(key, "<key>", StringComparison.Ordinal);

        if (values.Thumbprint is { Length: >= MinimumSecretLength } thumbprint)
            text = text.Replace(thumbprint, "<thumbprint>", StringComparison.OrdinalIgnoreCase);

        if (values.UserProfile is { Length: > 3 } userProfile)
        {
            foreach (var spelling in new[] { userProfile, userProfile.Replace('\\', '/') }.Distinct())
                text = text.Replace(spelling, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        }

        if (values.ServerHost?.Trim() is { Length: > 0 } host && !IsLoopback(host))
        {
            text = Regex.Replace(
                text,
                $@"(?<![\w.-]){Regex.Escape(host)}(?<port>:\d{{1,5}})?(?![\w-]|\.\w)",
                m => "<server>" + m.Groups["port"].Value,
                RegexOptions.IgnoreCase);
        }

        text = WholeWord(text, values.UserName, "<user>");
        text = WholeWord(text, values.MachineName, "<machine>");

        text = MongoId().Replace(text, m => "id-" + Short(m.Value));

        text = Address().Replace(text, m =>
            m.Groups["ip"].Value is "127.0.0.1" or "0.0.0.0" ? m.Value : "<ip>:" + m.Groups["port"].Value);

        return text;
    }

    private static string WholeWord(string text, string? word, string replacement) =>
        word is { Length: >= MinimumNameLength }
            ? Regex.Replace(text, $@"(?<![\w]){Regex.Escape(word)}(?![\w])", replacement, RegexOptions.IgnoreCase)
            : text;

    private static bool IsLoopback(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host is "127.0.0.1" or "0.0.0.0" or "::1";

    private static string Short(string id) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(id.ToLowerInvariant())))[..6];
}

public sealed record RedactionValues
{
    public string? UserProfile { get; init; }

    public string? UserName { get; init; }

    public string? MachineName { get; init; }

    public string? ServerHost { get; init; }

    public string? SharedKey { get; init; }

    public string? Thumbprint { get; init; }
}
