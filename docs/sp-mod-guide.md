**TCF Mod Manager** is a Windows desktop app for finding, installing and keeping track of your SPT mods, built directly against the sp-mod catalog you're reading this on. Browse the full mod list, filter it down to what actually works on your SPT version, install with dependencies resolved for you, turn mods on and off without deleting anything, and see at a glance what's out of date.

It also keeps **mod lists** - save the set you run, switch between sets, send one to a friend, or follow the list a server publishes and know what you are missing before you launch. Plus a config editor, a start button for the server and the launcher, and an optional page that reads what each installed mod actually ships.

## The full guide is on the wiki
**[github.com/TheCrimsonFckr/TCFModManager/wiki](https://github.com/TheCrimsonFckr/TCFModManager/wiki)** - every page of the app, mod lists, configs, Monitor mode, update notifications, the Server Map, limitations and troubleshooting. This page only covers getting it installed.

Inside the app, the **Help** page in the sidebar has short step-by-step answers for every page, in your language. Press **?** in the title bar or **F1** to open it at the page you're on, and **Open the full guide** takes you to the wiki.

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

## Reporting a problem
Use the [Issues tab](https://sp-mod.com/mod/2945/tcf-mod-manager#issues) - bugs, wording, translations, feature requests or questions. Attach `Data\logs\tcfmm-<date>.log`, ideally after adding the `verbose` marker file and reproducing the problem. The [wiki](https://github.com/TheCrimsonFckr/TCFModManager/wiki/Troubleshooting) covers the common ones first.

## Disclaimer
I use AI to help write this mod, everything is initially written by myself (I'm a developer by profession), then passed though Claude (Fable model) for refinements and to highlight any additional changes the LLM thinks are worth making. Absolutely everything is reviewed and tested by myself before I commit anything. This is done because I simply do not have enough time in day to make full refinements and multiple passes myself. If you are not comfortable using an application that uses AI as a tool then please do not download it.
