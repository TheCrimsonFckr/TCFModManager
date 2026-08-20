# TCFModManager

A Windows desktop app for finding, installing and keeping track of [SPT](https://sp-tarkov.com)
mods, built against [sp-mod](https://sp-mod.com) catalog. Browse the full mod list, filter it
down to what actually works on your SPT version, install with dependencies resolved for you, and
see at a glance what's out of date.

WPF with Fluent Design, .NET 9, no account or API key needed.

> Standalone for now. Integration with [TCFModSync](https://github.com/TheCrimsonFckr/TCFModSync)
> (pushing a managed mod set out to clients) is the direction of travel — manage first, then sync.

## Requirements

- Windows
- An SPT install (the app reads its version from the server exe — `SPT.Server.exe`, or the older
  `Aki.Server.exe`, at the install root or under `SPT\` / `SPT_Runtime\`)

The released build is self-contained, so no separate .NET runtime install is needed.

## Install

1. Grab the latest `TCF-ModManager-<version>.zip` from Releases.
2. Extract it into your SPT folder as `<SPT root>\TCFModManager\` — a sibling of `BepInEx\` and
   `user\`, **not** a mod folder. Anywhere else works too; it just needs to know where SPT is.
3. Run `TCFModManager.exe`.
4. Go to **Options**, point it at your SPT install folder, and hit Save. The detected server
   version appears underneath — everything else keys off that.

## What it does

**Browse** — the whole sp-mod.com catalog, fetched once and cached to disk. Search by name or by
author (`@author`), filter by SPT release line, category, Fika compatibility, featured status, and
toggles for hiding ads and AI-generated content. Cards show a status dot (installed / update
available / not installed / nothing compatible published) and a badge for mods that pull in
dependencies. Clicking a card opens its details; "Refresh cache" re-pulls the catalog.

**Installed** — scans your SPT folder for what's actually there, both client mods
(`BepInEx\plugins`) and server mods (`user\mods`), and matches them back to the catalog. Same
search and filters as Browse, plus an update-status filter. Each card shows the installed version,
the latest published one, and the folder it lives in when that differs from the mod name. Clicking
a card opens a dialog with the full version history (changelogs rendered from sp-mod's rich
text), a link to the mod page, and an Update button when one applies. Mods can be removed from
here too.

**Dependencies** — resolves the dependency tree of every installed mod that declares one, and
reports each dependency's state against what's on disk, including version conflicts where two mods
want incompatible versions of the same thing. Missing dependencies can be installed straight from
the list.

**Downloads** — the install queue. Items process one at a time; each resolves its dependencies and
queues those alongside it. Progress, per-item cancel, and "Clear finished". Cancelling a mod also
cancels the dependencies it dragged in. The page also handles plain archive downloads when you'd
rather install something by hand.

**Options** — SPT install folder, and the detected SPT version.

Before anything is queued, the app asks you to open each mod's page on sp-mod.com first — same as
installing manually, and it keeps mod authors' page views and instructions in the loop.

### How installs work

The archive is downloaded and extracted into a hidden scratch folder inside your SPT install
(`.tcfmm-work\`, swept of stale runs each time), then moved into place. When you're updating,
the previous version is only removed **after** the new one has downloaded and extracted
successfully — a failed or cancelled download can't leave you with neither. Once files start
being placed, the operation runs to completion rather than tearing out a half-installed mod.

Every install is recorded in `Data\installed-mods.json`, which is what makes a clean uninstall
possible later. That file — not folder names, not DLL file versions — is the authority on what's
installed and at what version.

### Where it keeps things

Everything lives next to the exe, not in `%LocalAppData%`:

| Path | What |
| --- | --- |
| `Data\settings.json` | SPT install path and app settings |
| `Data\installed-mods.json` | What this app installed, and every file it placed |
| `Data\mod_cache.json` | Cached catalog |
| `Data\spt_versions.json` | Cached SPT release list (refetched daily) |
| `Data\dependency_flags.json` | Per-mod "has dependencies" answers, re-checked when a mod publishes |
| `Data\logs\tcfmm-<date>.log` | Daily log |
| `Staging\` | Default destination for manually downloaded archives |

An older `%LocalAppData%\TCFModManagement\` layout is migrated automatically on first run.

### Logging

Info level by default. Drop an empty file named `verbose` (no extension) next to the exe to get
Debug-level output in the same log. Logs rotate daily as `tcfmm-<yyyyMMdd>.log`.

---

# Developer notes

## Layout

- `src/TCFModManager.Core` — sp-mod API client, models, and all the non-UI services (install,
  download, extraction, scanning, version matching, caching, logging). Plain `net9.0`, no UI
  dependencies, so it's reusable from tests, a console tool, or a future TCFModSync integration.
  One package reference: SharpCompress.
- `src/TCFModManager.App` — the WPF shell (`net9.0-windows`, [WPF-UI](https://www.nuget.org/packages/WPF-UI)
  4.3.0 for Fluent Design, MVVM via CommunityToolkit.Mvvm). Five pages: Browse, Installed,
  Dependencies, Downloads, Options.
- `Tests/TCFModManager.Core.Tests` — xunit tests over the API client, version matching,
  dependency status, and the installed-mod scanner, using JSON fixtures captured from live
  sp-mod.com responses.

Service wiring is a static `AppServices` holder rather than a DI container.

## sp-mod API

Base URL `https://sp-mod.com`, read-only and unauthenticated. Documented at
https://sp-mod.com/docs/index.html, OpenAPI spec at https://sp-mod.com/docs/openapi.yaml.

`SpModApiClient` (namespace `TCFModManager.Core.SpModApi`) covers the documented surface: mods,
mod versions, update and dependency resolution, version file trees, addons and their
versions/dependencies, categories, and SPT versions. Rate limiting is enforced at Cloudflare's edge
(documented as 40 requests/10s burst, 200/60s sustained); a 429 surfaces as
`SpModApiRateLimitedException` carrying the `Retry-After` value, and other failures as
`SpModApiException` with the status code and error code.

**The docs' generated examples don't always match live responses.** Confirmed by probing the real
endpoints while building this — the models and tests are built against the live shape:

- `License` returns only `id`, `hub_id`, `name`, `link`, `created_at`, `updated_at`. The documented
  `short_name` doesn't exist.
- The embedded `Category` on a mod (`include=category`) uses `title`, not `name`, and has no
  `color_class`.
- `/mods` accepts `include=category,versions`, but `include=dependencies` and the nested
  `include=versions.dependencies` both return **HTTP 400** — the list endpoint can't carry
  dependency data at all. Dependency info is only reachable per-mod via
  `GET /mod/{id}/versions?include=dependencies`, which is why Browse's dependency badge needs its
  own lookup and disk cache.
- The version objects embedded by `include=versions` on `/mods` are fuller than the docs suggest:
  they also carry `description`, `link`, `content_length` and `fika_compatibility`.

Mod version constraints are resolved against the live SPT release list rather than parsed as
semver ranges, so the UI names releases that actually exist instead of the boundary versions the
constraints are written in terms of.

## Building

Needs the .NET 9 SDK, and Windows for `TCFModManager.App` (WPF doesn't build on Linux/macOS).
Open `TCFModManager.sln`, or:

```
dotnet build TCFModManager.sln
dotnet test Tests/TCFModManager.Core.Tests
```

`build/` holds the shared MSBuild config — `Directory.Build.props` (the root one is a stub that
imports it, so MSBuild's directory walk-up still finds it) and `NuGet.config` — plus two scripts
that aren't tracked in git (`*.ps1` is gitignored, same convention as TCFModSync):

- `build\deploy.ps1` — forces a full rebuild and launches the exe, for a fast local test cycle.
- `build\package-release.ps1 -Version 0.3.5` — publishes a self-contained single-file win-x64
  build, zips it (plus a source zip via `git archive`) into `dist\`, and deploys that build into
  `<SptRoot>\TCFModManager\`. The SPT path comes from `-SptPath`, otherwise from Options' saved
  `settings.json`; if neither resolves, deploy is skipped and packaging still succeeds. Pass
  `-SkipDeploy` to always skip it.

The published exe is ~135MB, almost entirely the bundled .NET 9 runtime and WPF framework
assemblies rather than app code. `SatelliteResourceLanguages` is already set to trim non-English
resources. `InvariantGlobalization` is **not** usable here — WPF's XAML binding pipeline calls
`CultureInfo.GetCultureInfo("en-US")` on startup and invariant mode throws.

### Editor showing false `InitializeComponent` errors

`deploy.ps1` always builds Release. On a fresh clone that means the generated
`obj\Release\...\Views\*.g.cs` partials exist but the Debug equivalents don't, and the C# language
service analyzes Debug by default — so `*.xaml.cs` files light up with "`InitializeComponent` does
not exist" / "`x:Name` does not exist" until you build Debug once:

```
dotnet build src\TCFModManager.App\TCFModManager.App.csproj -c Debug
```

May need a language-server restart to pick up. The Release build was never broken; it's purely
tooling looking at the wrong configuration's output.

## Known gaps

- `InstalledModScannerTests.Scan_ChecksBothBepInExPluginsAndPatchers` fails. `InstalledModScanner`
  scans `BepInEx\plugins` and the `user\mods` layouts but has no `BepInEx\patchers` scan — the test
  asserts behaviour that was never implemented, rather than a regression.
- No DI container. `AppServices` is a static holder; worth swapping for
  `Microsoft.Extensions.DependencyInjection` plus WPF-UI's `INavigationService`/`IPageService` if
  the page count grows.
- No app icon.
- `build\Directory.Build.props` still declares `<Version>0.1.0</Version>` while releases are cut at
  0.3.x, and the API client's default User-Agent is still `TCFModManager/0.1`.
