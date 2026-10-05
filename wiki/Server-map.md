An optional page - **off by default**, turned on in Options - that connects to an SPT server running the **Server Map mod** and shows what that server runs, so you can be ready before you launch rather than after a raid fails to load.

- **The mod goes on the server**, not on your machine. It is a separate download, published as an addon of this mod - **Options - Server map connection - Get the Server Map mod** opens its page - and a player joining a server needs none of it.
- **Nothing about your install is sent without asking.** Connecting asks the server who it is. With the Server Map mod 0.2.0 the page asks once whether to **Share** this machine on the server's map; say no and nothing is sent. **Options - Server map connection** has the same answer as a switch, and the name the machine shows.
- **The server serves a list, never files.** Mods are still only ever downloaded from sp-mod.com. A server that could push files at you would break the one rule this app is built on, so there is no route for it to do so.
- **Certificates are pinned on first use.** SPT serves a self-signed certificate, so the app remembers the exact one your server presented and tells you if it ever changes - which is what a machine-in-the-middle would look like. Trust the new one or refuse it.
- **A shared key** guards everything but the handshake. The operator gives it to you; on the server's own machine the app finds it by itself.
- **LAN-only, for the operator.** On the server machine, **Options - Server map connection - Only answer this network** makes the server refuse every request from outside its own network - Tailscale peers still count as inside.

**Connecting fetches the list for you** and saves it as a read-only mod list, marked as coming from that server; it is fetched again on its own whenever the server's revision moves. **Fetch again** asks for it even when the revision hasn't moved, for a copy you have edited or deleted.

Saving is not applying. From there it is an ordinary list - preview it on the Mod lists page, apply it, keep your own alongside it - and the Play page's check compares against it before you launch.

**The map** lists every machine that shares itself with the server, the server first and yours marked *This machine*: whether each is in game, has the app open or was last seen some time ago, and whether it has what the server's list asks for - named, with versions, in the usual status colours. When your own card is behind, **Review and install** takes you to the fix. Only machines running this app appear, and the map needs the Server Map mod 0.2.0 on the server.

## If you run the server
The Server Map mod's own page carries the operator guide - installing the payload and the stub for your SPT line, where the key and the published list live (`TCFModManager\Data\ServerMap\`), rotating the key, and opening a port. The mod list half - capturing, pruning, publishing, and the one thing not to apply on the machine that hosts - is on [Mod lists](Mod-lists).

> [!WARNING]
> Client and server must be on the same SPT line: a 4.1 client cannot join a 4.0.13 server, and that is SPT's rule rather than this app's. A published list only ever reaches people already on your version.

The whole Server Map guide - installing the mod, publishing a list, joining and troubleshooting - starts at [Server Map guide](Server-Map-guide).
