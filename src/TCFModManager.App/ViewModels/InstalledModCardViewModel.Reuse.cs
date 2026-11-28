using System.Reflection;
using System.Runtime.CompilerServices;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

public sealed partial class InstalledModCardViewModel
{
    private static readonly PropertyInfo[] ScanProperties =
    [
        .. typeof(InstalledModCardViewModel)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.SetMethod is { IsPublic: true } setter
                && setter.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit))),
    ];

    public void TakeScanFrom(InstalledModCardViewModel fresh)
    {
        foreach (var property in ScanProperties) property.SetValue(this, property.GetValue(fresh));

        _duplicateFolders = null;
        OnPropertyChanged(string.Empty);
    }

    public static List<InstalledModCardViewModel> KeepUnchanged(
        IReadOnlyList<InstalledModCardViewModel> current,
        IReadOnlyList<InstalledModCardViewModel> fresh,
        out int kept)
    {
        var currentByKey = Unique(current);
        var freshKeys = Unique(fresh);

        var result = new List<InstalledModCardViewModel>(fresh.Count);
        kept = 0;

        foreach (var card in fresh)
        {
            var key = ModGroupStore.KeyFor(card.Name);
            if (freshKeys.ContainsKey(key)
                && currentByKey.TryGetValue(key, out var existing)
                && existing.IsAddon == card.IsAddon)
            {
                existing.TakeScanFrom(card);
                result.Add(existing);
                kept++;
            }
            else
            {
                result.Add(card);
            }
        }

        return result;
    }

    private static Dictionary<string, InstalledModCardViewModel> Unique(IReadOnlyList<InstalledModCardViewModel> cards) =>
        cards
            .GroupBy(c => ModGroupStore.KeyFor(c.Name))
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.First());
}
