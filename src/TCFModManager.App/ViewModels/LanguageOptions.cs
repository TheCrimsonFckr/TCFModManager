using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using TCFModManager.App.Localization;

namespace TCFModManager.App.ViewModels;

//
// One entry in the Options page's Language dropdown. A null Culture is the "System default" entry,
// playing the part ThemePreference.FollowSystem plays in the Theme dropdown.
//
// A class with a live Label rather than the labelled record the Theme dropdown uses, because these
// labels are themselves translated: choosing a language has to relabel the entries in that language,
// and a string captured when the list was built would stay in whichever language built it. Being a
// LocalizedViewModel is what makes the relabelling happen - Label is computed, so it is re-read.
//
// A language is named in itself - Deutsch, not German - which is what a language picker has to do:
// somebody looking for their own language cannot be expected to recognise its English name.
//
public sealed partial class LanguageOptionItem(CultureInfo? culture) : LocalizedViewModel
{
    public CultureInfo? Culture { get; } = culture;

    // What goes into settings.json. Null for System default, which is what "no preference" is
    // stored as.
    public string? Tag => Culture?.Name;

    public string Label => Culture is null
        ? string.Format(
            CultureInfo.CurrentCulture,
            Strings.Options_Language_SystemDefaultFormat,
            AppLanguage.DisplayName(AppLanguage.SystemDefault))
        : AppLanguage.DisplayName(Culture);
}
