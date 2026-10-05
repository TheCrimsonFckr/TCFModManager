**You need**

- Windows
- An SPT install

**Steps**

1. Download `TCF-ModManager-<version>.zip` from [its sp-mod page](https://sp-mod.com/mod/2945/tcf-mod-manager).
2. Extract it into your SPT folder as `<SPT root>\TCFModManager\` - a sibling of `BepInEx\` and `user\`.
3. Run `TCFModManager.exe`.
4. Open **Options**, point it at your SPT install folder - the top one, with `EscapeFromTarkov.exe` and `BepInEx\` in it - and hit Save. The detected server version appears underneath - everything else keys off that.

> [!WARNING]
> This is **not** a mod. Don't extract it into `BepInEx\plugins` or `user\mods`. It's a standalone application that manages that folder for you. Anywhere on disk works, really - it just needs to be told where SPT lives.

Once it's set up, the app keeps itself up to date from [its own page on sp-mod](https://sp-mod.com/mod/2945/tcf-mod-manager) - see [App updates](App-updates).

## If the SPT version doesn't detect
The version is read from the server executable - `SPT.Server.exe`, or the older `Aki.Server.exe` - at the install root or under `SPT\` / `SPT_Runtime\`. Point Options at the install root, the folder with `EscapeFromTarkov.exe` in it, not at the server folder or any other subfolder. If you do pick `SPT\` or `SPT_Runtime\` itself, the app moves the setting up to the folder above it.
