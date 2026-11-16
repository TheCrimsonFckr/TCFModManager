using TCFModManager.App.Localization;
using TCFModManager.App.ViewModels;
using TCFModManager.App.Views;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

public static class UnsavedConfigEdits
{
    internal static ConfigsViewModel? Page { get; set; }

    public static bool AllowExit()
    {
        if (Page is not { HasDirty: true } page) return true;

        var choice = AskWindow.Show(new AskRequest
        {
            Title = Strings.Configs_DiscardTitle,
            Lead = LocalizationService.Text(Strings.Configs_QuitUnsavedFormat, TextLists.Join(page.DirtyFileNames())),
            First = Strings.Configs_SaveAllAndQuit,
            Second = Strings.Configs_QuitWithoutSaving,
            Cancel = Strings.Configs_KeepEditing,
            Danger = AskChoice.Second,
        });

        switch (choice)
        {
            case AskChoice.First:
                page.SaveAllCommand.Execute(null);
                return !page.HasDirty;
            case AskChoice.Second:
                AppLog.Info("Configs", "quitting with unsaved config edits dropped, as asked");
                return true;
            default:
                return false;
        }
    }
}
