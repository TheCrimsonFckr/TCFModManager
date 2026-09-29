using TCFModManager.App.Localization;
using TCFModManager.App.Views;
using TCFModManager.Core.Services;
using Wpf.Ui.Controls;

namespace TCFModManager.App.Help;

//
// What the Help page holds and in what order: sections, the how-tos in each, and the steps of each
// how-to. The words are in Strings.resx; this file is only the shape.
//
// Every string is a Func rather than a string, read each time it is shown, so the page follows a
// language change the way the rest of the app does.
//
// A step is a format string, and its {0}, {1}... are filled from the label of the control the step
// names - the same key that control reads. That keeps a step's button name identical to the button
// on screen in every language, and after any rename, without a translator having to find it.
//
internal static class HelpCatalog
{
    public static IReadOnlyList<HelpSection> Sections { get; } =
    [
        new("start", () => Strings.Help_Start_Title, SymbolRegular.Rocket24, null,
        [
            Topic("start.setup", () => Strings.Help_Start_Setup_Title,
                Step(() => Strings.Help_Start_Setup_Step1, () => Strings.Nav_Options),
                Step(() => Strings.Help_Start_Setup_Step2,
                    () => Strings.Options_InstallFolderHeader, () => Strings.Options_Browse),
                Step(() => Strings.Help_Start_Setup_Step3, () => Strings.Common_Save))
                .WithNote(() => Strings.Help_Start_Setup_Note),

            Topic("start.version", () => Strings.Help_Start_Version_Title,
                Step(() => Strings.Help_Start_Version_Step1),
                Step(() => Strings.Help_Start_Version_Step2,
                    () => Strings.Nav_Options, () => Strings.Options_InstallFolderHeader),
                Step(() => Strings.Help_Start_Version_Step3, () => Strings.Common_Save)),
        ]),

        new("browse", () => Strings.Nav_Browse, SymbolRegular.Apps24, typeof(BrowsePage),
        [
            Topic("browse.compatible", () => Strings.Help_Browse_Compatible_Title,
                Step(() => Strings.Help_Browse_Compatible_Step1, () => Strings.Nav_Browse),
                Step(() => Strings.Help_Browse_Compatible_Step2),
                Step(() => Strings.Help_Browse_Compatible_Step3, () => Strings.Common_ClearFilters)),

            Topic("browse.install", () => Strings.Help_Browse_Install_Title,
                Step(() => Strings.Help_Browse_Install_Step1, () => Strings.Nav_Browse),
                Step(() => Strings.Help_Browse_Install_Step2,
                    () => Strings.ReadModPage_ButtonOpen, () => Strings.Common_Continue),
                Step(() => Strings.Help_Browse_Install_Step3, () => Strings.Common_Continue),
                Step(() => Strings.Help_Browse_Install_Step4, () => Strings.Nav_Downloads))
                .WithNote(() => Strings.Help_Browse_Install_Note),

            Topic("browse.author", () => Strings.Help_Browse_Author_Title,
                Step(() => Strings.Help_Browse_Author_Step1, () => Strings.Nav_Browse),
                Step(() => Strings.Help_Browse_Author_Step2)),

            Topic("browse.hideinstalled", () => Strings.Help_Browse_HideInstalled_Title,
                Step(() => Strings.Help_Browse_HideInstalled_Step1,
                    () => Strings.Nav_Browse, () => Strings.Filter_AnyMod, () => Strings.Filter_HideInstalled),
                Step(() => Strings.Help_Browse_HideInstalled_Step2, () => Strings.Common_SaveAsDefault))
                .WithNote(() => Strings.Help_Browse_HideInstalled_Note),
        ]),

        new("installed", () => Strings.Nav_Installed, SymbolRegular.CheckmarkCircle24, typeof(InstalledPage),
        [
            Topic("installed.updates", () => Strings.Help_Installed_Updates_Title,
                Step(() => Strings.Help_Installed_Updates_Step1, () => Strings.Nav_Installed),
                Step(() => Strings.Help_Installed_Updates_Step2,
                    () => Strings.Filter_UpdateAny, () => Strings.Filter_UpdateNeeded)),

            Topic("installed.update", () => Strings.Help_Installed_Update_Title,
                Step(() => Strings.Help_Installed_Update_Step1,
                    () => Strings.Nav_Installed, () => Strings.Installed_DetailsAndVersions),
                Step(() => Strings.Help_Installed_Update_Step2,
                    () => Strings.ModUpdate_Update, () => Strings.ModUpdate_Redownload),
                Step(() => Strings.Help_Installed_Update_Step3, () => Strings.Common_Continue))
                .WithNote(() => Strings.Help_Installed_Update_Note,
                    () => Strings.Installed_MultiSelect, () => Strings.Installed_UpdateSelected),

            Topic("installed.views", () => Strings.Help_Installed_Views_Title,
                Step(() => Strings.Help_Installed_Views_Step1,
                    () => Strings.Nav_Installed, () => Strings.Installed_ViewCards,
                    () => Strings.Installed_ViewGroups, () => Strings.Installed_ViewList),
                Step(() => Strings.Help_Installed_Views_Step2)),

            Topic("installed.groups", () => Strings.Help_Installed_Groups_Title,
                Step(() => Strings.Help_Installed_Groups_Step1,
                    () => Strings.Nav_Installed, () => Strings.Installed_ViewGroups),
                Step(() => Strings.Help_Installed_Groups_Step2,
                    () => Strings.Installed_NewGroupPlaceholder, () => Strings.Installed_AddGroup),
                Step(() => Strings.Help_Installed_Groups_Step3))
                .WithNote(() => Strings.Help_Installed_Groups_Note),

            Topic("installed.disable", () => Strings.Help_Installed_Disable_Title,
                Step(() => Strings.Help_Installed_Disable_Step1, () => Strings.Nav_Installed),
                Step(() => Strings.Help_Installed_Disable_Step2),
                Step(() => Strings.Help_Installed_Disable_Step3))
                .WithNote(() => Strings.Help_Installed_Disable_Note),

            Topic("installed.disablemany", () => Strings.Help_Installed_DisableMany_Title,
                Step(() => Strings.Help_Installed_DisableMany_Step1,
                    () => Strings.Nav_Installed, () => Strings.Installed_MultiSelect),
                Step(() => Strings.Help_Installed_DisableMany_Step2, () => Strings.Installed_DisableSelected))
                .WithNote(() => Strings.Help_Installed_DisableMany_Note, () => Strings.Installed_ViewGroups),

            Topic("installed.undo", () => Strings.Help_Installed_Undo_Title,
                Step(() => Strings.Help_Installed_Undo_Step1,
                    () => Strings.Nav_Installed, () => Strings.Installed_Undo),
                Step(() => Strings.Help_Installed_Undo_Step2)),
        ]),

        new("modlists", () => Strings.Nav_ModLists, SymbolRegular.AppsList24, typeof(ModListsPage),
        [
            Topic("modlists.capture", () => Strings.Help_ModLists_Capture_Title,
                Step(() => Strings.Help_ModLists_Capture_Step1, () => Strings.Nav_ModLists),
                Step(() => Strings.Help_ModLists_Capture_Step2,
                    () => Strings.ModLists_CaptureHeader, () => Strings.ModLists_Capture))
                .WithNote(() => Strings.Help_ModLists_Capture_Note),

            Topic("modlists.apply", () => Strings.Help_ModLists_Apply_Title,
                Step(() => Strings.Help_ModLists_Apply_Step1,
                    () => Strings.Nav_ModLists, () => Strings.ModLists_SavedHeader),
                Step(() => Strings.Help_ModLists_Apply_Step2, () => Strings.ModLists_Preview),
                Step(() => Strings.Help_ModLists_Apply_Step3, () => Strings.ModLists_Apply))
                .WithNote(() => Strings.Help_ModLists_Apply_Note),

            Topic("modlists.pin", () => Strings.Help_ModLists_Pin_Title,
                Step(() => Strings.Help_ModLists_Pin_Step1,
                    () => Strings.Nav_Installed, () => Strings.Installed_Pin),
                Step(() => Strings.Help_ModLists_Pin_Step2, () => Strings.Installed_Unpin)),

            Topic("modlists.edit", () => Strings.Help_ModLists_Edit_Title,
                Step(() => Strings.Help_ModLists_Edit_Step1,
                    () => Strings.Nav_ModLists, () => Strings.ModLists_AddMods),
                Step(() => Strings.Help_ModLists_Edit_Step2, () => Strings.Common_Save),
                Step(() => Strings.Help_ModLists_Edit_Step3, () => Strings.ModLists_Apply))
                .WithNote(() => Strings.Help_ModLists_Edit_Note, () => Strings.ModLists_MakeCopy),

            Topic("modlists.share", () => Strings.Help_ModLists_Share_Title,
                Step(() => Strings.Help_ModLists_Share_Step1,
                    () => Strings.Nav_ModLists, () => Strings.ModLists_Export),
                Step(() => Strings.Help_ModLists_Share_Step2,
                    () => Strings.ModLists_Import, () => Strings.Nav_ModLists),
                Step(() => Strings.Help_ModLists_Share_Step3,
                    () => Strings.ModLists_Preview, () => Strings.ModLists_Apply))
                .WithNote(() => Strings.Help_ModLists_Share_Note),

            Topic("modlists.server", () => Strings.Help_ModLists_Server_Title,
                Step(() => Strings.Help_ModLists_Server_Step1,
                    () => Strings.Nav_Options, () => Strings.Options_SectionPages,
                    () => Strings.Options_ServerMapHeader),
                Step(() => Strings.Help_ModLists_Server_Step2,
                    () => Strings.Options_ServerMapConnectionHeader, () => Strings.Options_Connect),
                Step(() => Strings.Help_ModLists_Server_Step3,
                    () => Strings.Nav_ServerMap, () => Strings.Common_ReviewAndInstall),
                Step(() => Strings.Help_ModLists_Server_Step4, () => Strings.ModLists_Apply))
                .WithNote(() => Strings.Help_ModLists_Server_Note),

            Topic("modlists.undo", () => Strings.Help_ModLists_Undo_Title,
                Step(() => Strings.Help_ModLists_Undo_Step1,
                    () => Strings.Nav_ModLists,
                    () => LocalizationService.Text(Strings.ModLists_UndoLabelFormat, "…")),
                Step(() => Strings.Help_ModLists_Undo_Step2)),
        ]),
    ];

    private static HelpTopic Topic(string id, Func<string> title, params HelpStep[] steps) =>
        new(id, title, steps);

    private static HelpStep Step(Func<string> format, params Func<string>[] labels) =>
        new(format, labels);
}

// One sidebar page's worth of how-tos, or Getting started. PageType is the page the "?" opens this
// section from; null for a section that isn't a page.
internal sealed record HelpSection(
    string Id,
    Func<string> Title,
    SymbolRegular Icon,
    Type? PageType,
    IReadOnlyList<HelpTopic> Topics);

internal sealed record HelpTopic(string Id, Func<string> Title, IReadOnlyList<HelpStep> Steps)
{
    public HelpStep? Note { get; private init; }

    public HelpTopic WithNote(Func<string> format, params Func<string>[] labels) =>
        this with { Note = new HelpStep(format, labels) };
}

internal sealed record HelpStep(Func<string> Format, IReadOnlyList<Func<string>> Labels);

// The pieces a step renders as: plain text, and the control labels it names in bold.
internal readonly record struct HelpRun(string Text, bool IsLabel);

internal static class HelpText
{
    //
    // Splits "Press {0}, then {1}." into runs, the placeholders replaced by their labels.
    //
    // Done by hand rather than with string.Format so the labels can come out bold, and so a
    // translation whose placeholders don't match what the catalog passes shows the text it has
    // instead of throwing. The test that every translation keeps English's placeholders is what
    // stops that happening; this only keeps it from taking the page down if it ever does.
    //
    public static IReadOnlyList<HelpRun> Runs(HelpStep step)
    {
        var format = step.Format();
        var runs = new List<HelpRun>();
        var text = new System.Text.StringBuilder();
        var i = 0;

        while (i < format.Length)
        {
            var c = format[i];

            if (c == '{' && i + 1 < format.Length && format[i + 1] == '{')
            {
                text.Append('{');
                i += 2;
                continue;
            }

            if (c == '}' && i + 1 < format.Length && format[i + 1] == '}')
            {
                text.Append('}');
                i += 2;
                continue;
            }

            if (c == '{')
            {
                var close = format.IndexOf('}', i);
                if (close > i
                    && int.TryParse(format.AsSpan(i + 1, close - i - 1), out var index)
                    && index >= 0 && index < step.Labels.Count)
                {
                    if (text.Length > 0) runs.Add(new HelpRun(text.ToString(), false));
                    text.Clear();
                    runs.Add(new HelpRun(step.Labels[index](), true));
                    i = close + 1;
                    continue;
                }

                AppLog.Warn("Help", $"step placeholder doesn't match its labels: {format}");
            }

            text.Append(c);
            i++;
        }

        if (text.Length > 0) runs.Add(new HelpRun(text.ToString(), false));
        return runs;
    }

    public static string Plain(HelpStep step) => string.Concat(Runs(step).Select(r => r.Text));
}
