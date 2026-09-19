using System.Globalization;
using System.Runtime.InteropServices;
using TCFModManager.App.Localization;
using TCFModManager.Core.Services;

namespace TCFModManager.App;

//
// Resolves which language the app reads in, and applies it.
//
// The sibling of AppTheme: the stored preference lives in settings.json, "not set" means follow
// Windows, and the rules for what that means stay in one place rather than being spread between
// App, the Options page and whatever reads a string.
//
// Text follows the language chosen here; dates and numbers do not. CurrentUICulture is what
// resource lookup reads, and it is the only one this file sets - CurrentCulture stays whatever
// Windows' regional setting says, because somebody reading the app in one language while their PC
// formats dates another way has already made that choice elsewhere.
//
public static class AppLanguage
{
    // The language every build ships complete, and the end of every fallback chain.
    public const string DefaultTag = "en";

    // Stateless (it only holds a path), so constructing one costs nothing and every write does its
    // own Load first - the same load-mutate-save the rest of the app uses on this file.
    private static readonly SettingsService Settings = new();

    // Ask for the tags as BCP-47 names ("en-GB") rather than LCID hex - MUI_LANGUAGE_NAME.
    private const uint MuiLanguageName = 0x8;

    //
    // What resource lookup actually uses. Passed explicitly at every lookup rather than left to
    // CurrentUICulture: DefaultThreadCurrentUICulture only reaches threads started after it is set,
    // and the thread pool this app runs its scans and downloads on is older than that.
    //
    public static CultureInfo Current { get; private set; } = CultureInfo.GetCultureInfo(DefaultTag);

    // Raised after Current changes, so bindings can re-read. LocalizationService turns this into the
    // notification every {loc:Str} binding listens for.
    public static event EventHandler? Changed;

    //
    // The languages this build has resources for, English first.
    //
    // Read once: a satellite assembly cannot appear while the app is running, and the alternative -
    // asking ResourceManager about every culture .NET knows - is a few hundred failed satellite
    // probes for an answer that is already written down.
    //
    public static IReadOnlyList<CultureInfo> Available { get; } = ReadShipped();

    // The stored preference as written. Null or absent means follow Windows.
    public static string? Stored => Settings.Load().Language;

    //
    // Applies the stored preference. Called from App.OnStartup before anything is loaded, so the
    // first frame is drawn in the right language rather than being re-read a moment later.
    //
    public static void ApplyStored() => Apply(Resolve(Stored));

    //
    // Applies a newly chosen preference and saves it. Null is "System default" - the entry that
    // plays the part ThemePreference.FollowSystem plays for theme.
    //
    public static void Set(string? tag)
    {
        var settings = Settings.Load();
        settings.Language = string.IsNullOrWhiteSpace(tag) ? null : tag.Trim();
        Settings.Save(settings);

        Apply(Resolve(settings.Language));

        AppLog.Info("Language", $"set to {settings.Language ?? "system default"} ({Current.Name})");

        Changed?.Invoke(null, EventArgs.Empty);
    }

    //
    // What a language calls itself, for the dropdown. Read out of that language's own resources, so
    // it arrives with the translation rather than being a list here that has to be kept in step.
    //
    public static string DisplayName(CultureInfo culture)
    {
        try
        {
            var name = Strings.ResourceManager.GetString("Meta_LanguageName", culture);
            if (!string.IsNullOrWhiteSpace(name)) return name;
        }
        catch (Exception ex)
        {
            AppLog.Warn("Language", $"couldn't read the name of {culture.Name}: {ex.Message}");
        }

        return culture.NativeName;
    }

    //
    // Which shipped language a tag means, or null for one nothing here covers.
    //
    // Walks up the parents, which is what makes one file serve a family: ru-RU, ru-UA and ru-KZ all
    // land on ru, and zh-Hans-CN on zh-Hans, without any of them being listed.
    //
    public static CultureInfo? Match(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;

        CultureInfo culture;
        try
        {
            culture = CultureInfo.GetCultureInfo(tag.Trim());
        }
        catch (CultureNotFoundException)
        {
            return null;
        }

        // InvariantCulture is its own parent, so this is what ends the walk rather than a depth.
        for (var c = culture; !string.IsNullOrEmpty(c.Name); c = c.Parent)
        {
            var shipped = Available.FirstOrDefault(
                a => string.Equals(a.Name, c.Name, StringComparison.OrdinalIgnoreCase));

            if (shipped is not null) return shipped;
        }

        return null;
    }

    //
    // First hit wins: the stored preference, then the user's Windows language list in their own
    // order, then English.
    //
    // The middle step is the reason this is not one line. CurrentUICulture is only the top entry of
    // that list, so a machine set to a language we have no resources for would land on English even
    // when the user's own second choice is one we do have.
    //
    // A stored tag with nothing behind it - a language removed from a later build, or a typo in a
    // hand-edited settings.json - falls through to the same place as never having chosen one, which
    // is the rule this file already follows for a page default that no longer parses.
    //
    private static CultureInfo Resolve(string? stored)
    {
        if (Match(stored) is { } pinned) return pinned;

        foreach (var preferred in PreferredUiLanguages())
        {
            if (Match(preferred) is { } match) return match;
        }

        return CultureInfo.GetCultureInfo(DefaultTag);
    }

    private static void Apply(CultureInfo culture)
    {
        Current = culture;

        CultureInfo.CurrentUICulture = culture;

        // So work started on a fresh thread after this point reads the same language. Threads that
        // already exist keep whatever they were born with, which is why Current is passed explicitly
        // at lookup rather than relied on here.
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    //
    // The user's Windows display languages, in the order they put them in.
    //
    // Two calls: the first with no buffer asks how big one needs to be, the second fills it. The
    // result is a double-null-terminated block of tags.
    //
    private static IReadOnlyList<string> PreferredUiLanguages()
    {
        try
        {
            uint length = 0;

            if (!GetUserPreferredUILanguages(MuiLanguageName, out _, null, ref length) || length == 0)
            {
                return [CultureInfo.CurrentUICulture.Name];
            }

            var buffer = new char[length];
            if (!GetUserPreferredUILanguages(MuiLanguageName, out _, buffer, ref length))
            {
                return [CultureInfo.CurrentUICulture.Name];
            }

            return new string(buffer, 0, (int)length)
                .Split('\0', StringSplitOptions.RemoveEmptyEntries)
                .ToList();
        }
        catch (Exception ex)
        {
            // A language list we couldn't read is worth a line and nothing more - the resolution
            // below it still ends somewhere sensible, and no part of startup should stop for this.
            AppLog.Warn("Language", $"couldn't read the Windows language list: {ex.Message}");
            return [CultureInfo.CurrentUICulture.Name];
        }
    }

    //
    // Meta_ShippedLanguages out of the neutral file, deliberately: read with the current language
    // instead, a translator who filled that key in would be changing which languages the app offers.
    //
    private static IReadOnlyList<CultureInfo> ReadShipped()
    {
        var english = CultureInfo.GetCultureInfo(DefaultTag);
        var shipped = new List<CultureInfo> { english };

        try
        {
            var raw = Strings.ResourceManager.GetString("Meta_ShippedLanguages", CultureInfo.InvariantCulture);

            foreach (var tag in (raw ?? string.Empty).Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    var culture = CultureInfo.GetCultureInfo(tag.Trim());

                    if (!shipped.Any(c => string.Equals(c.Name, culture.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        shipped.Add(culture);
                    }
                }
                catch (CultureNotFoundException)
                {
                    AppLog.Warn("Language", $"{tag} is listed as shipped but is not a language tag");
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("Language", $"couldn't read the shipped language list: {ex.Message}");
        }

        return shipped;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetUserPreferredUILanguages(
        uint dwFlags,
        out uint pulNumLanguages,
        [Out] char[]? pwszLanguagesBuffer,
        ref uint pcchLanguagesBuffer);
}
