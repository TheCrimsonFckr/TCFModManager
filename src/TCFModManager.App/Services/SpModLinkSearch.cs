using System.Windows;
using System.Windows.Controls;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

internal static class SpModLinkSearch
{
    public static void Attach(TextBox box) =>
        DataObject.AddPastingHandler(box, (_, e) =>
        {
            if (e.DataObject.GetData(DataFormats.UnicodeText, true) is not string text) return;
            if (TextFor(text) is not { } search) return;

            e.CancelCommand();
            box.Text = search;
            box.CaretIndex = search.Length;
        });

    public static string? TextFor(string? text)
    {
        if (SpModPageLink.Parse(text) is not { Kind: not SpModLinkKind.List } link) return null;

        var name = link.Kind == SpModLinkKind.Mod
            ? AppServices.ModCache.AllMods.FirstOrDefault(m => m.Id == link.Id)?.Name
            : AppServices.Addons.AllAddons.FirstOrDefault(a => a.Id == link.Id)?.Name;

        if (!string.IsNullOrWhiteSpace(name)) return name.Trim();
        return link.Slug?.Replace('-', ' ');
    }
}
