using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

//
// OPEN-72: sidebar items that can be switched off in Options. Shared so the sidebar and the Options
// switches read one answer.
//
public sealed class NavItemsViewModel : ObservableObject
{
    private const string ModListsKey = "modlists";
    private const string ConfigsKey = "configs";
    private const string DependenciesKey = "dependencies";
    private const string DiagnoseKey = "diagnose";
    private const string QuickPlayKey = "quickplay";
    private const string BackForwardKey = "backforward";

    private readonly HashSet<string> _hidden =
        new(new SettingsService().Load().HiddenNavItems, StringComparer.OrdinalIgnoreCase);

    public bool ShowModLists { get => Shown(ModListsKey); set => Set(ModListsKey, value); }

    public bool ShowConfigs { get => Shown(ConfigsKey); set => Set(ConfigsKey, value); }

    public bool ShowDependencies { get => Shown(DependenciesKey); set => Set(DependenciesKey, value); }

    public bool ShowDiagnose { get => Shown(DiagnoseKey); set => Set(DiagnoseKey, value); }

    public bool ShowQuickPlay { get => Shown(QuickPlayKey); set => Set(QuickPlayKey, value); }

    public bool ShowBackForward { get => Shown(BackForwardKey); set => Set(BackForwardKey, value); }

    private bool Shown(string key) => !_hidden.Contains(key);

    private void Set(string key, bool shown, [CallerMemberName] string? property = null)
    {
        var changed = shown ? _hidden.Remove(key) : _hidden.Add(key);
        if (!changed) return;

        var service = new SettingsService();
        var settings = service.Load();
        settings.HiddenNavItems = _hidden.Order(StringComparer.Ordinal).ToList();
        service.Save(settings);

        OnPropertyChanged(property);
        AppLog.Info("Sidebar", $"{key} {(shown ? "shown" : "hidden")}");
    }
}
