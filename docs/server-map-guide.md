# Server Map

Server Map lets an SPT server publish the mod list it expects players to be running, so TCF Mod
Manager can tell someone what they are missing **before** they launch instead of after a raid fails
to load. It also shows **who is on the server**: every machine that agrees to report, whether it is
in game, and whether it has what the list asks for.

Two halves, and they are separate installs:

**The Server Map mod** - goes on the machine running the SPT server. Installed by the server operator.

**The Server Map page** - lives in TCF Mod Manager. Switched on by anyone joining that server.

It never sends mod files - mods still only ever download from sp-mod.com. It never reports your
install without asking, and it never blocks you playing.

## The full guide is on the wiki

**[Server Map guide](https://github.com/TheCrimsonFckr/TCFModManager/wiki/Server-Map-guide)** -
publishing a list, scopes and Fika headless, the shared key, keeping it to your own network, joining
a server, the map, and troubleshooting. This page only covers getting the mod installed.

- [If you run the server](https://github.com/TheCrimsonFckr/TCFModManager/wiki/Server-Map-hosting)
- [If you are joining a server](https://github.com/TheCrimsonFckr/TCFModManager/wiki/Server-Map-joining)
- [When something is wrong](https://github.com/TheCrimsonFckr/TCFModManager/wiki/Server-Map-troubleshooting)

## Install the Server Map mod

**There is one zip per SPT line** - `...-SPT4.1.zip` and `...-SPT4.0.13.zip`. Take the one that
matches your server; if you take the wrong one, SPT refuses to load it and says so. Client and server
must be on the same SPT line anyway: a 4.1 client cannot join a 4.0.13 server.

1. **Stop the server.** The payload DLL is locked while it runs, and a copy over a locked file
   fails silently.
2. **Extract the zip into your SPT root** - the folder that holds `SPT_Runtime\` (4.1) or `SPT\`
   (4.0), and `TCFModManager\`. The **stub** lands in `user\mods\TCFMM.ServerMap\`; the
   **payload** lands in `TCFModManager\ServerMap\payload\`.
3. **Start the server** and watch its console for:

```
[TCFMM ServerMap] Ready - serving /tcfservermap from <path>
```

Your server's key is then in `TCFModManager\Data\ServerMap\servermap-key.txt`. Send it to your
players with the address and port. **Keep `Data\`** when you update or redeploy - your key and
published list live there.

#### warning
**Do not apply your published list on the machine that hosts.** A list pruned down to what players
need sets aside your server's own mods - this one included. Keep two lists on the server box: the
full one you apply there, and the pruned one you publish. The wiki explains why.

## Joining a server

Nothing to download. In TCF Mod Manager: **Options** → **Pages** → switch **Server map** on, then
**Options** → **Fika and servers** → **Server map connection**, fill in the address, port and key
the operator sent you, and press **Connect**.
