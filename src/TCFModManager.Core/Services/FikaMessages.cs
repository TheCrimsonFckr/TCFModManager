using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TCFModManager.Core.Services;

public enum FikaProblem
{
    RequiredModsMissing,
    ModsNotVerified,
    ServerModMissing,
    ServerTooOld,
    FikaVersionDiffers,
    EftVersionDiffers,
    FikaAltered,
}

/// <summary>One of Fika's own messages as the installed Fika words it, and the pattern built from it.</summary>
public sealed record FikaMessage(FikaProblem Problem, string Format, Regex Pattern);

//
// OPEN-54 N32 (Q7b): Fika's own messages about mods and versions, read from the installed Fika -
// the literal strings in Fika.Core.dll and the locales of the fika-server mod - so a log line is
// matched in the words that Fika version uses, in any of its languages. A message whose anchor or
// key can't be found is logged, never guessed at.
//
public sealed class FikaMessages
{
    private static readonly (FikaProblem Problem, string Anchor)[] CodeAnchors =
    [
        (FikaProblem.RequiredModsMissing, "missing required mod(s)"),
        (FikaProblem.ModsNotVerified, "Failed to verify mods with server"),
        (FikaProblem.ServerModMissing, "Make sure that the server mod is installed"),
        (FikaProblem.ServerTooOld, "Server version check failed"),
    ];

    private static readonly (FikaProblem Problem, string Key)[] LocaleKeys =
    [
        (FikaProblem.FikaVersionDiffers, "F_UI_FikaVersionError"),
        (FikaProblem.EftVersionDiffers, "F_UI_EFTVersionError"),
        (FikaProblem.FikaAltered, "F_Mod_VerificationFailure"),
    ];

    public IReadOnlyList<FikaMessage> Messages { get; }

    public IReadOnlyList<FikaProblem> NotFound { get; }

    private FikaMessages(IReadOnlyList<FikaMessage> messages, IReadOnlyList<FikaProblem> notFound)
    {
        Messages = messages;
        NotFound = notFound;
    }

    // Null when this install has no Fika.Core.dll.
    public static FikaMessages? Read(string? installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath) || CoreDll(installPath) is not { } dll) return null;

        var found = From(UserStrings(dll), LocaleValues(installPath));
        if (found.NotFound.Count > 0)
            AppLog.Info("Diagnose", $"Fika's messages not found in the installed Fika: {string.Join(", ", found.NotFound)}");
        return found;
    }

    public static FikaMessages From(IEnumerable<string> codeStrings, IEnumerable<(string Key, string Value)> locale)
    {
        var strings = codeStrings.ToList();
        var values = locale.ToList();
        var messages = new List<FikaMessage>();
        var missing = new List<FikaProblem>();

        foreach (var (problem, anchor) in CodeAnchors)
        {
            var format = strings.FirstOrDefault(s => s.Contains(anchor, StringComparison.Ordinal));
            if (format is null) missing.Add(problem);
            else messages.Add(new FikaMessage(problem, format, PatternFor(format)));
        }

        foreach (var (problem, key) in LocaleKeys)
        {
            var formats = values.Where(v => v.Key == key && !string.IsNullOrWhiteSpace(v.Value)).Select(v => v.Value).Distinct().ToList();
            if (formats.Count == 0) missing.Add(problem);
            foreach (var format in formats) messages.Add(new FikaMessage(problem, format, PatternFor(format)));
        }

        return new FikaMessages(messages, missing);
    }

    // The problem and the values Fika put in its message, in order; null when no message matches.
    public (FikaProblem Problem, IReadOnlyList<string> Args)? Match(string text)
    {
        foreach (var message in Messages)
        {
            var m = message.Pattern.Match(text);
            if (!m.Success) continue;

            var args = m.Groups.Cast<Group>()
                .Where(g => g.Name.StartsWith('a') && int.TryParse(g.Name.AsSpan(1), out _))
                .OrderBy(g => int.Parse(g.Name.AsSpan(1)))
                .Select(g => g.Value.Trim())
                .ToList();
            return (message.Problem, args);
        }

        return null;
    }

    // "{0} missing required mod(s): {1}" -> a pattern capturing each placeholder up to the end of its line.
    public static Regex PatternFor(string format)
    {
        var seen = new HashSet<string>();
        var escaped = Regex.Replace(Regex.Escape(format.Replace("\r\n", "\n")), @"(?:\\ )*\\n", @"[ \t]*\r?\n");
        var body = Regex.Replace(escaped, @"\\\{(\d+)}", m =>
            seen.Add(m.Groups[1].Value) ? $"(?<a{m.Groups[1].Value}>[^\\r\\n]*?)" : $"\\k<a{m.Groups[1].Value}>");
        return new Regex(body + "(?=\\r?\\n|$)", RegexOptions.Multiline | RegexOptions.CultureInvariant);
    }

    private static string? CoreDll(string installPath)
    {
        var plugins = Path.Combine(installPath, "BepInEx", "plugins");
        if (!Directory.Exists(plugins)) return null;

        try
        {
            return Directory.EnumerateFiles(plugins, "Fika.Core.dll", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 3 })
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Debug("Diagnose", $"couldn't look for Fika.Core.dll: {ex.Message}");
            return null;
        }
    }

    private static List<string> UserStrings(string dll)
    {
        var strings = new List<string>();
        try
        {
            using var stream = new FileStream(dll, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata) return strings;

            var reader = pe.GetMetadataReader();
            var handle = MetadataTokens.UserStringHandle(1);
            while (!handle.IsNil)
            {
                strings.Add(reader.GetUserString(handle));
                handle = reader.GetNextHandle(handle);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException or InvalidOperationException)
        {
            AppLog.Debug("Diagnose", $"couldn't read {dll}: {ex.Message}");
        }

        return strings;
    }

    // Every language's value of the keys looked for, from the fika-server mod's locales.
    private static List<(string Key, string Value)> LocaleValues(string installPath)
    {
        var values = new List<(string, string)>();
        var keys = LocaleKeys.Select(k => k.Key).ToHashSet(StringComparer.Ordinal);

        foreach (var container in DisabledModPaths.ServerContainers(installPath).Where(Directory.Exists))
        {
            try
            {
                foreach (var mod in Directory.EnumerateDirectories(container))
                {
                    var global = Path.Combine(mod, "assets", "database", "locales", "global");
                    if (!Directory.Exists(global)) continue;

                    foreach (var file in Directory.EnumerateFiles(global, "*.json"))
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(File.ReadAllText(file),
                                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                            if (doc.RootElement.ValueKind != JsonValueKind.Object) continue;

                            foreach (var property in doc.RootElement.EnumerateObject())
                                if (keys.Contains(property.Name) && property.Value.ValueKind == JsonValueKind.String)
                                    values.Add((property.Name, property.Value.GetString()!));
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                        {
                            AppLog.Debug("Diagnose", $"couldn't read {file}: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Debug("Diagnose", $"couldn't list {container}: {ex.Message}");
            }
        }

        return values;
    }
}
