**TCF Mod Manager** is a Windows desktop app for finding, installing and keeping track of your SPT mods, built directly against the sp-mod catalog you're reading this on. Browse the full mod list, filter it down to what actually works on your SPT version, install with dependencies resolved for you, turn mods on and off without deleting anything, and see at a glance what's out of date.

It also keeps **mod lists** - save the set you run, switch between sets, send one to a friend, or follow the list a server publishes and know what you are missing before you launch. Plus a config editor, a start button for the server and the launcher, and an optional page that reads what each installed mod actually ships.

## The full guide is on the wiki
**[github.com/TheCrimsonFckr/TCFModManager/wiki](https://github.com/TheCrimsonFckr/TCFModManager/wiki)** - every page of the app, mod lists, configs, Monitor mode, update notifications, the Server Map, limitations and troubleshooting. This page covers what it does and getting it installed.

Inside the app, the **Help** page in the sidebar has short step-by-step answers for every page, in your language. Press **?** in the title bar or **F1** to open it at the page you're on, and **Open the full guide** takes you to the wiki.

## Features
**Finding and installing**

- **Browse** the whole sp-mod catalog - search by name, teaser or `@author`, filter by SPT version, category, Fika compatibility, publish or update date and authors you follow, and pin the filters you use most under the search bar
- **Install** with dependencies resolved and queued for you - up to three downloads at once, with retry and resume, and a bar along the bottom of the window while the queue runs
- **Addons** listed under their mod, checked against the version you actually have
- **Author pages** - every mod by an author, and follow them to hear about their new releases
- Mod descriptions and changelogs shown as sp-mod shows them - tabs, tables and GIFs

**Keeping track**

- **Installed** - everything in your SPT folder matched back to sp-mod, in Cards, Groups or List view, with what's out of date at a glance
- An **Update** button on every card, a right-click menu on every mod, and multi-select to update, enable, disable or remove several at once
- **Switch to any version** - update, downgrade or redownload from the version history
- **Disable** a mod without deleting anything - its files and settings come back exactly as they were
- **Dependencies and Conflicts** - missing or wrong-version dependencies, mods installed twice, and clashing copies of the same file
- **SPT upgrade check** - pick a newer SPT and see which of your mods are ready for it
- **Held-back updates** - an update that would break another installed mod isn't offered, and the card says why

**Mod lists**

- Save the set you run, switch between sets, and send one to a friend
- Import a public sp-mod Mod List from its link
- **Pin** the mods you always want on, so no list you apply sets them aside
- Preview exactly what applying a list will do before anything moves

**Playing**

- **Play** starts the server, the launcher and a Fika headless, with a check before launch
- **Configs** - edit client, server and BepInEx settings in the app, with your settings kept when a mod updates
- **Server Map** (optional, with the Server Map mod on the server) - see what a server runs and get on the same list before you join

**Safety**

- An install that is interrupted is never left half-done - the app shows it as partly installed so a reinstall puts it right
- Your SPT profiles are backed up before any install, update, remove, disable or list apply, and can be put back from Options
- Warned before installing a version sp-mod marks as not working with Fika, and before removing a mod that may have changed your profile
- SPT's own files are never overwritten or removed

**And**

- **Monitor mode** - let the app download each mod for you to install by hand, and still keep track of what you have
- **Update notifications** - told when a mod you have gets a new release, even from the tray
- **Mod footprint** (optional) - what each installed mod's files contain, read from disk without running anything
- **Help** built in, with step-by-step answers for every page
- English, Deutsch, Français, Italiano and Русский
- Keeps itself up to date from this page

## Install
**You need**

- Windows
- An SPT install

The released build is entirely self-contained - nothing else to install.

**Steps**

1. Download `TCF-ModManager-<version>.zip` from this page.
2. Extract it into your SPT folder as `<SPT root>\TCFModManager\` - a sibling of `BepInEx\` and `user\`.
3. Run `TCFModManager.exe`.
4. Open **Options**, point it at your SPT install folder - the top one, with `EscapeFromTarkov.exe` and `BepInEx\` in it - and hit Save. The detected server version appears underneath - everything else keys off that.

#### warning
This is **not** a mod. Don't extract it into `BepInEx\plugins` or `user\mods`. It's a standalone application that manages that folder for you. Anywhere on disk works, really - it just needs to be told where SPT lives. Keep it out of `Program Files`, where Windows won't let it update itself.

#### If the SPT version doesn't detect
The version is read from the server executable - `SPT.Server.exe`, or the older `Aki.Server.exe` - at the install root or under `SPT\` / `SPT_Runtime\`. Point Options at the install root, the folder with `EscapeFromTarkov.exe` in it, not at the server folder or any other subfolder. If you do pick `SPT\` or `SPT_Runtime\` itself, the app moves the setting up to the folder above it.

## Updating
Once it's set up, the app keeps itself up to date from this page. On launch it checks for a newer release and shows a banner and a badge on **App update** in the sidebar; you're asked to open this page before anything is downloaded. Your settings, install history and kept configs survive an update.

## Planning
Coming next, in no particular order - the [Planning page](https://github.com/TheCrimsonFckr/TCFModManager/wiki/Planning) on the wiki has the full list, including everything already done.

- Nothing new lined up right now - ideas are welcome on the [Issues tab](https://sp-mod.com/mod/2945/tcf-mod-manager#issues)

## Reporting a problem
Use the [Issues tab](https://sp-mod.com/mod/2945/tcf-mod-manager#issues) - bugs, wording, translations, feature requests or questions. Attach `Data\logs\tcfmm-<date>.log`, ideally after adding the `verbose` marker file and reproducing the problem. The [wiki](https://github.com/TheCrimsonFckr/TCFModManager/wiki/Troubleshooting) covers the common ones first.

## Disclaimer
I use AI to help write this mod, everything is initially written by myself (I'm a developer by profession), then passed though Claude (Fable model) for refinements and to highlight any additional changes the LLM thinks are worth making. Absolutely everything is reviewed and tested by myself before I commit anything. This is done because I simply do not have enough time in day to make full refinements and multiple passes myself. If you are not comfortable using an application that uses AI as a tool then please do not download it.
