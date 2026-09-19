using System.Windows.Data;
using System.Windows.Markup;

namespace TCFModManager.App.Localization;

//
// {loc:Str Installed_Rescan} - a string from Strings.resx, in the language currently chosen.
//
// Hands back a binding rather than the string itself, which is the whole point: a plain string
// would be read once when the page is parsed and stay in whichever language that was. Bound to
// LocalizationService, the same markup re-reads when the language changes, so an open page follows
// the dropdown without being rebuilt - and the pages that cache themselves (NavigationCacheMode)
// keep their filters and scroll position through it.
//
// XAML only. In C#, read Strings.Installed_Rescan, where the key is checked by the compiler.
//
[MarkupExtensionReturnType(typeof(string))]
public sealed class StrExtension : MarkupExtension
{
    public StrExtension()
    {
    }

    public StrExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]")
        {
            Source = LocalizationService.Instance,
            Mode = BindingMode.OneWay,
        };

        // ProvideValue rather than the binding itself, so this works in the places a binding has to
        // be resolved differently - a Style setter, a template - as well as on a plain property.
        return binding.ProvideValue(serviceProvider);
    }
}
