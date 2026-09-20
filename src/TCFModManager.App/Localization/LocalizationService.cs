using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Localization;

//
// The one object every {loc:Str} binding in the app reads from.
//
// ResourceManager re-reads on every call, so a string looked up after the language changes is
// already correct - but a WPF binding has no reason to look again. This is the thing it binds to:
// an indexer, and one notification saying every index changed, which is what makes a language
// switch repaint the open page instead of needing every page rebuilt.
//
// C# call sites do not come here. They read Strings.Common_Rescan directly, which the compiler
// checks; this exists for XAML, where the key is a string and nothing would catch a typo (the test
// in TCFModManager.Core.Tests is what catches it).
//
public sealed class LocalizationService : INotifyPropertyChanged
{
    public static LocalizationService Instance { get; } = new();

    private LocalizationService()
    {
        // Binding.IndexerName is "Item[]" - WPF's way of saying every indexed value on this object
        // may have changed, which re-evaluates every {loc:Str} binding in one go.
        AppLanguage.Changed += (_, _) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Binding.IndexerName));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string this[string key] => Get(key);

    //
    // A key with nothing behind it renders as the key itself - visible, searchable, and obviously
    // wrong, rather than a blank label nobody notices. A key missing only from a translation never
    // reaches this: resource fallback hands back the English value.
    //
    public static string Get(string key)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;

        try
        {
            return Strings.ResourceManager.GetString(key, AppLanguage.Current) ?? key;
        }
        catch (Exception ex)
        {
            AppLog.Warn("Language", $"couldn't read {key}: {ex.Message}");
            return key;
        }
    }
}
