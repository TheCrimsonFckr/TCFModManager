# ServerMap — spike build (S1 + S6)

Not the feature. This is the smallest thing that answers the two spikes the design gates everything
else on, in one deployment:

- **S6** — does the thin stub / fat payload split work under SPT's mod loader? Does DI still find the
  stub's listener, and is the stub inert (one log line, no exception) when the payload folder is
  deleted?
- **S1** — can a non-game HTTP client on another machine reach a mod's route?

It answers exactly one route, `GET /tcfservermap/hello`, and holds no state.

## Built against SPT 4.1, which is not SPT 4.0

Three interfaces changed, and a 4.0 mod does not compile — never mind load — on 4.1:

| 4.0.13 | 4.1 |
|---|---|
| `AbstractModMetadata` (abstract record), `IsBundleMod` | `IModMetadata` (interface), `HasPrepatcher` |
| `IOnLoad.OnLoad()` | `IOnLoad.OnLoadAsync(CancellationToken)` |
| `IHttpListener.CanHandle(MongoId, HttpContext)` / `.Handle(...)` | `.CanHandle(HttpContext)` / `.HandleAsync(MongoId, HttpContext, CancellationToken)` |

SPT 4.1.5 also targets **.NET 10**, so these projects are `net10.0` and need the .NET 10 SDK.

## Building

`SptServerDir` must point at the folder holding `SPT.Server.exe` and the `SPTarkov.*.dll` files —
`G:\Single Player Tarkov 4.1\SPT_Runtime` here. Set it in `build/Directory.Build.local.props`, or as
the `SPT_SERVER_DIR` environment variable, or pass it on the command line:

```
dotnet build src/ServerMap/ServerMap.Server/ServerMap.Server.csproj -c Release ^
  -p:SptServerDir="G:\Single Player Tarkov 4.1\SPT_Runtime"
```

The SPT assemblies are referenced from the install rather than NuGet on purpose: 4.1.5 is not
published there (newest package is 4.1.2), and these are the exact assemblies the server will load
this mod beside. All are `Private=false`, so none are copied into the mod folder.

These projects are deliberately **not in `TCFModManager.sln`** while this is a spike — adding them
would break a plain `dotnet build` at the repo root for anyone without an SPT install.

## Deploying

Two folders, and the split is the point:

```
<SPT root>\SPT_Runtime\user\mods\TCFMM.ServerMap\      <- ServerMap.Server.Stub\bin\Release\TCFMM.ServerMap\
<SPT root>\TCFModManager\ServerMap\payload\            <- ServerMap.Server\bin\Release\payload\
```

The stub finds the payload by walking up from its own folder looking for
`TCFModManager\ServerMap\payload`, so it does not care which SPT layout it is in.

## What to look for

Start the server. In its log:

- **Loaded:** `[TCFMM ServerMap] Ready - serving /tcfservermap from <path>`
- **Payload deleted:** `[TCFMM ServerMap] No payload found, so this mod does nothing. Looked for
  TCFMM.ServerMap.Payload.dll in: <every folder it checked>` — and the server starts normally. That
  is S6's second half: deleting the folder removes the feature without breaking the server.
- **Found but unloadable:** an error naming the exception. If it complains about casting
  `IServerMapPayload` to `IServerMapPayload`, the stub got loaded twice and `Assembly.LoadFrom` is
  the wrong loader — that is a real S6 finding, not a bug to paper over.

Then, from the other machine:

```
curl -k https://<server-lan-ip>:6969/tcfservermap/hello
```

`-k` is required: SPT serves a self-signed certificate for `CN=localhost`, so both the chain and the
hostname fail validation. That is expected, and is what the design's trust-on-first-use thumbprint
pinning exists to handle properly.

A JSON body back is S1 answered. It carries `payloadPath`, so a reply proves the **payload** served
it rather than the stub.

**Before any of this**, `SPT_Data/configs/http.json` must not be bound to loopback: `ip` to
`0.0.0.0`, `backendIp` to the machine's LAN address, and TCP 6969 allowed inbound.
