using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.Views;

namespace TCFModManager.App.ViewModels;

public partial class InstalledViewModel
{
    private void RefreshNotes()
    {
        var notes = AppServices.ModNotes.Load();
        foreach (var card in _all)
            card.Note = notes.TryGetValue(Core.Services.ModGroupStore.KeyFor(card.Name), out var note) ? note : null;
    }

    [RelayCommand]
    private void EditNote(InstalledModCardViewModel? mod)
    {
        if (mod is null) return;
        if (ModListNameWindow.AskForNote(mod.DisplayTitle, mod.Note) is not { } text) return;

        AppServices.ModNotes.Set(mod.Name, text);
        RefreshNotes();
        ApplyFilter();
        StatusMessage = Text(text.Length == 0 ? Strings.Installed_NoteClearedFormat : Strings.Installed_NoteSavedFormat, mod.DisplayTitle);
    }
}
