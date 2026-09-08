# Server Map how do I…?

Server Map lets an SPT server publish the mod list it expects players to be running, so TCF Mod
Manager can tell someone what they are missing **before** they launch instead of after a raid fails
to load.

Two halves, and they are separate installs:

**The Server Map mod** > goes on the machine running the SPT server. Installed by the server operator.

**The Server Map page** > lives in TCF Mod Manager. Installed by anyone joining that server.


#### Three things it does not do:
- It never sends mod files. The server publishes a *list*; mods still only ever download from
  sp-mod.com. There is no route for a server to push you a file.
- It never reports anything about your install to the server. Connecting asks the server who it is.
  Nothing goes the other way.
- It never blocks you playing. The check on the Play page says what it found and leaves the launch
  buttons working.

Client and server must be on the same SPT line a 4.1 client cannot join a 4.0.13 server, and that
is SPT's rule, not this mod's. So a published list only ever reaches people already on your version.

# If you run the server 
How Do I?...

# {.tabset}
## Get the Server Map mod?

It is **not bundled with TCF Mod Manager** it is a separate download, published as an addon on the
app's mod page at sp-mod.com. That is deliberate: it is installed on a different machine from the
app, by a different person, and a player joining your server needs none of it.

In the app: **Options** → **Server map** → **Get the Server Map mod**. That opens the page in your
browser.

**Download the build that matches your SPT version.** There are two and they are not
interchangeable:

Your server is SPT **4.0.13** Download the **4.0** build
Your server SPT **4.1.x** Download the **4.1** build

If you install the wrong one, SPT refuses to load it and says so it will not half-work.

## Install the Server Map mod?

You need two folders. The **stub** goes where SPT looks for mods; the **payload** goes outside it,
so removing the feature later is one folder deletion.

1. **Stop the server.** The payload DLL is locked while it runs, and a copy over a locked file
   fails silently you will then spend an hour debugging the previous build.
2. Copy the **stub folder** (`TCFMM.ServerMap`, containing `TCFMM.ServerMap.Stub.dll`,
   `TCFMM.ServerMap.Shared.dll` and the `.deps.json`) into `user\mods\` the `user` folder that
   sits beside `SPT.Server.exe`.
3. Copy the **payload** (`TCFMM.ServerMap.Payload.dll`) into a folder called
   `TCFModManager\ServerMap\payload\`.
4. **Start the server.**

## Where exactly does the payload folder go?

The stub starts in its own folder and walks **up** the tree looking for
`TCFModManager\ServerMap\payload\`, so it does not care which SPT layout you have. Two real
examples:

```
SPT 4.0.13
  \SPT\user\mods\TCFMM.ServerMap\   <- stub
  \TCFModManager\ServerMap\payload\ <- payload
  \TCFModManager\Data\ServerMap\    <- key and published list

SPT 4.1.x
  \SPT_Runtime\user\mods\TCFMM.ServerMap\ <- stub
  \TCFModManager\ServerMap\payload\       <- payload
  \TCFModManager\Data\ServerMap\          <- key and published list
```

If TCF Mod Manager is already installed on that machine, it lives in that same `TCFModManager`
folder so `ServerMap\payload\` goes right next to the app.

**`Data\` is the folder to keep.** Your key and your published list live there, with the app's
settings and install history. Replacing `TCFModManager\` wholesale when you deploy takes all of it
with you lay a new build over the old one instead, the way the app's own updater does.

## Know when it is loaded?

Watch the server's console on startup:

```
[TCFMM ServerMap] Ready - serving /tcfservermap from <path>
```

That is the only line you need. Two others tell you what went wrong:

- `No payload found, so this mod does nothing. Looked for TCFMM.ServerMap.Payload.dll in: …` the
  stub loaded but cannot find the payload. The message lists every folder it checked; put the
  payload in one of them. **The server starts normally**, which is the point: deleting the payload
  folder is the supported way to switch the feature off.
- `Found <path> but could not load it: …` the payload is there but broken. If it complains about
  casting `IServerMapPayload` to `IServerMapPayload`, there is a stray copy of
  `TCFMM.ServerMap.Shared.dll` in the payload folder. There should not be one; delete it.

## Find my server's key?

Every route except the handshake needs a shared key, so a stranger who finds your port cannot read
your list. The server generates one **on startup** before anyone connects, because it is the
thing you send out with the address.

It is in a text file in the app's data folder:

```
<...>\TCFModManager\Data\ServerMap\servermap-key.txt
```

24 characters in six groups of four. Send it to your players along with the address and port. It is
compared with dashes, spaces and case ignored, so it does not matter how they paste it.

The key is generated **once**, the first time the server starts without one. After that it is left
alone restarting the server does not change it, and neither does updating the mod. The file is the
key: whatever is in it is what the server expects.

**If TCF Mod Manager is on the same machine as the server**, you never need to open this file: the
app reads it and fills the key in on its own. See "How do I fill in my own server's details".

## Change my server's key?

**Options** → **Server map** → **Generate a new key**. It only appears on the machine actually
running the server.

Everyone holding the old key stops being able to see what your server publishes the moment you press
it, so only do this if a key has gone somewhere it should not have and send the new one out
afterwards.

The server picks the change up on its own. **It does not need restarting.**

## Publish a mod list to my server?

1. Get the server's install the way you want it, then on **Mod lists** press **Capture** to make a
   list of what is there. (Or pick an existing list.)
2. Check the scopes see the next question. This is the step people skip and regret.
3. With the list selected, press **Publish to this server**.

That writes the list into `TCFModManager\Data\ServerMap\` on this machine. Nothing is installed,
enabled or disabled; no files on the game folder change.

The list is re-read whenever its file changes, so **publishing again takes effect without
restarting the server**.

The published list gets a red **Serving** badge on the Mod lists page, so among a dozen personal
lists you can see which one other people are being handed.

## Stop a server mod being sent to my players?

Mark it **Server only**.

The problem: your server needs `fika-server`, the Server Map mod itself, and anything else living
in `user\mods`. None of those are on The Forge, and none of them belong on a player's machine. But
if you leave them off your list entirely, applying your own list on the server switches them off.

So every entry on a list has a **scope**:

| Scope | Means |
|---|---|
| **Everyone** | both sides install it the normal case |
| **Client only** | only the player installs it |
| **Server only** | only the server has it; clients skip it completely |

A **Server only** entry is not fetched by a client, not counted as missing, and never appears in
their "you are behind" list.

Scope is worked out for you when you capture a list, from where the mod's files actually live. To
change one: select the list, find the row, and press the **scope button** (the two-people icon) to
cycle it Everyone → Client only → Server only → Everyone. Then **Save**, then **Publish to this
server** again.

## Update the list my server is serving?

Edit the list, **Save** it, then **Publish to this server** again. That is all the server picks
up the new file on its own.

Every apply bumps the list's **revision** number, and that number is what tells connected players
their copy is stale. They will see "the server is publishing revision 4; you have revision 3" on
their Play page.

## Let people connect from outside my network?

Server Map uses the SPT server's own port there is no second listener so if people can already
join your server, they can already reach it.

If you are setting that up from scratch, in `SPT_Data/configs/http.json`:

- `ip` → `0.0.0.0`
- `backendIp` → your machine's LAN address
- allow TCP **6969** inbound, and forward it if you want people outside your network

Then give people your external address, the port, and the key.

## Remove it?

Delete the `TCFModManager\ServerMap\payload\` folder. The server starts normally, the stub logs one
line and does nothing. Delete `user\mods\TCFMM.ServerMap\` too if you want it gone entirely.

{.endtabset}

# If you are joining a server
How do I?...

# {.tabset}
## Turn the Server map page on?

It is off by default, because without a server running the mod there is nothing for it to show.

**Options** → scroll to **Server map** → toggle it **On - shown in the sidebar**.

## Connect to a server?

In the same **Server map** section of Options:

1. **Server address** the same address you put in the SPT launcher. Not `0.0.0.0`: that is a bind
   address, not one you can dial.
2. **Port** 6969 unless the operator says otherwise.
3. **Shared key** paste what the operator sent you.
4. Press **Connect**.

The **Server map** page in the sidebar then shows the server's name, its SPT version, and whether it
publishes a list.

## Fill in my own server's details?

When the app is on the server machine? You do not. If a Server Map server is installed on this machine, 
the app finds its key file andfills the box in for you every time you open Options or the Server map page, 
so it appears the first time you start the server without restarting the app, and it follows the 
key if you rotate it.

**Use this machine's key** is still there for the one case that needs it: if you have typed a
different server's key into the box, the app leaves it alone rather than overwriting it, and that
button puts your own back.

On any machine that is not running a server, no file is found and the box stays empty which is
correct. A key belongs to one server.

## Get the server's mod list?

On the **Server map** page, press **Fetch again**.

The list is saved here as a new mod list, badged red as **From server** on the Mod lists page. It
stays exactly as the server wrote it you cannot edit it. If you want a version of your own, use
**Make a copy**.

## Install what the server expects?

Treat it like any other list:

1. **Mod lists** → select the list badged **From server**.
2. **Preview** to see exactly what would change.
3. **Apply** to queue the downloads.

Everything comes from sp-mod.com as usual, and anything the server marked **Server only** is
skipped you will not be asked to install `fika-server`.

## Keep my own mods as well as the server's?

You do not have to choose. A server's list and one of your own can be **active at the same time**,
which is what the two separate tick marks on the Mod lists page mean.

Following a server does not drop the personal list you were already following, and applying an
Exclusive list of your own will not sweep away the mods the server requires they are protected
from your own list's tidy-up.

## Check I am ready before I play?

Open the **Play** page. It checks automatically, and **Check again** re-runs it.

| What it says | What it means |
|---|---|
| **Ready to join** | your install matches the list, at the versions it names |
| **Your install doesn't match this server** | it names exactly which mods are outstanding |
| **This server's mod list has changed** | the server publishes a newer revision than you hold fetch it |
| **Couldn't check the server** | it did not answer; you are compared against your last fetched copy |

It never stops you launching. If it says you are three mods behind and you want to try anyway, the
buttons still work.

## Find one mod on a long list?

A served list can run to eighty mods. Above the list contents there is a **search box**, a **scope
filter** (All scopes / Everyone / Client only / Server only) and a **sort** (A-Z / Z-A).

Search matches the mod's name, the name the list stored, and the folder it installs into so you
can find something by the folder name if that is what you know it by.

The header reads "12 of 76 mods on this list" while a filter is on, so a filtered view is never
mistaken for a short list.

## Stop using a server's list?

Delete it on the Mod lists page, or apply one of your own. To stop connecting altogether, switch the
**Server map** toggle off in Options the page disappears from the sidebar and nothing is
contacted.

{.endtabset}

# When something is wrong 
Troubleshooting...

# {.tabset}
## The server does not have a published playlist

The server is running the mod but is not publishing anything. Either the operator has not published
one yet, or the file did not land in `TCFModManager\ServerMap\config\`. Operators: check the file is
there, and that there is either exactly one `.tcfmodlist` in the folder or one named
`published.tcfmodlist` several files with no preferred name is ambiguous, so the server serves
nothing rather than guessing.

## This server's certificate has changed

SPT serves a self-signed certificate, so the app remembers the exact one your server presented the
first time and checks it every time after. A change means one of three things:

- the operator reinstalled or moved the server, and it generated a new certificate normal
- you are connecting to a different machine than you think
- someone is sitting between you and the server

The app shows both fingerprints and will not connect until you decide. Ask the operator whether they
expected it. If yes, **Trust the new certificate**. If you are not sure, do not.

**Forget this certificate** clears what is remembered, so the next connection starts fresh.

## It says the key was rejected

Keys are compared ignoring dashes, spaces and case, so a formatting difference is not the cause. Ask
the operator to re-read `servermap-key.txt` it is regenerated only if the file is deleted, so a
mismatch usually means you have an old one.

## Nothing answers at all

Work outwards:

1. Can you join the server in SPT at all? If not, this was never going to work it uses the same
   port.
2. Is the address the one you use in the SPT launcher, and not `0.0.0.0`?
3. Did the server log the `Ready - serving /tcfservermap` line on startup?

{.endtabset}

# What it does not do yet

Worth knowing so you are not looking for it:

- **"Who else is on this map" is empty.** The server does not report the other machines connected to
  it. That needs a later version of the server mod, and it will ask before it reports anything about
  your machine.
- **One server per install.** The app connects to one server at a time.
- **Nothing is applied automatically.** A server can never change your install; every fetch, apply
  and download is something you press.
