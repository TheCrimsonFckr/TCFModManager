using TCFModManager.App.Localization;

namespace TCFModManager.App.Services;

//
// Sentences this app says in more than one place.
//
// Nothing goes here just for being user-facing - a message with a single caller reads better next
// to the code that raises it, and AppUpdateProblems already holds the self-updater's wording where
// it belongs. This file is specifically for the ones that had drifted into several copies, where
// the cost is not the duplication itself but that rewording one means finding all of them, and
// missing one leaves the app saying two different things about the same situation.
//
public static class AppMessages
{
    //
    // Every page that needs the SPT folder says this when it is not set: Installed, Browse,
    // Configs, Dependencies, Mod lists, the addon rows and the update dialog. Twelve copies before
    // this constant existed.
    //
    // It names the page to go to rather than only the problem, because the folder is set in exactly
    // one place and the user has no other way of knowing which.
    //
    // A property rather than a const since the text became a resource: a const is baked into every
    // call site at compile time, which would pin all of them to English.
    //
    public static string NoSptInstallFolder => Strings.Common_NoSptInstallFolder;
}
