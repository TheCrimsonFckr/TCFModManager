# TCFModManagement

A WPF (Fluent UI) companion to [TCFModSync](../TCFModSync) for finding and managing SPT mods
through [The Forge API](https://sp-mod.com/docs/index.html) - the read-only, unauthenticated
catalog behind sp-mod.com.

Direction: this app is standalone for now (manage mods against the Forge catalog), with
TCFModSync integration coming later (manage first, then sync to clients).

## Structure

- `src/TCFModManagement.Core` - the Forge API client, models, and services. No UI dependencies -
  usable from a console app, tests, or a future TCFModSync integration.
- `src/TCFModManagement.App` - the WPF app (net9.0-windows, WPF UI / Fluent Design, MVVM via
  CommunityToolkit.Mvvm). Four pages: Browse, Updates, Dependencies, Downloads.
- `Tests/TCFModManagement.Core.Tests` - xunit tests for the Forge API client and services, using
  JSON fixtures captured from live sp-mod.com responses (see note below).

## The Forge API

Base URL `https://sp-mod.com`, no auth required, rate limited at Cloudflare's edge (40 req/10s
burst, 200 req/60s sustained - `ForgeApiClient` throws `ForgeApiRateLimitedException` with the
`Retry-After` value on 429). Full docs: https://sp-mod.com/docs/index.html, OpenAPI spec at
https://sp-mod.com/docs/openapi.yaml.

`ForgeApiClient` (in `TCFModManagement.Core.ForgeApi`) covers every documented endpoint: mods,
mod versions, mod updates/dependencies resolution, mod version file trees, addons and their
versions/dependencies, mod categories, and SPT versions.

**A few fields in the API docs' generated examples don't match what the live API actually
returns** - confirmed by probing the real endpoints while building this:

- `License` only ever returns `id`, `hub_id`, `name`, `link`, `created_at`, `updated_at`. The
  docs' `short_name` field doesn't exist in practice.
- The embedded `Category` on a mod (`include=category`) returns `id`, `hub_id`, `title`, `slug`,
  `description` - not the `name`/`color_class` shape shown in one of the docs' examples.

The models in `Core/Models` and the tests in `ForgeApiClientTests.cs` are built against the real
shape (with fixtures captured 2026-08-13), not the docs' examples.

## Building

Requires the .NET 9 SDK and, for `TCFModManagement.App`, Windows (WPF can't be built on
Linux/macOS). Open `TCFModManagement.sln` in Visual Studio, or:

```
dotnet build TCFModManagement.sln
dotnet test Tests/TCFModManagement.Core.Tests
```

`build/` holds the shared MSBuild config (`Directory.Build.props`, `NuGet.config` - the root
`Directory.Build.props` is just a stub that imports it) plus two scripts, not tracked in git
(`*.ps1` is gitignored, same convention as TCFModSync):

- `build\deploy.ps1` - forces a full rebuild and launches the built exe, for a fast local test cycle.
- `build\package-release.ps1 -Version 0.1.0` - publishes a self-contained win-x64 build, zips it
  (plus a source zip via `git archive`) into `dist\`, and deploys that same build into
  `<SptRoot>\TCFModManagement\` - a sibling of `BepInEx\`/`user\`, not a mod folder. The SPT path
  comes from `-SptPath` if passed, otherwise from Options' saved `settings.json`; if neither
  resolves, deploy is skipped (packaging still succeeds). Pass `-SkipDeploy` to always skip it.

`deploy.ps1` always builds Release. On a fresh clone, that means `obj\Release\...\Views\*.g.cs`
(the generated partial classes with `InitializeComponent()`, from WPF's XAML markup compiler) exist,
but the Debug-configuration equivalents under `obj\Debug\...` don't. Visual Studio/VS Code's C#
language service analyzes against Debug by default, so it'll show false "`InitializeComponent`
does not exist" / "`x:Name` does not exist" errors on *.xaml.cs files until you build Debug at
least once:

```
dotnet build src\TCFModManagement.App\TCFModManagement.App.csproj -c Debug
```

(may need an IDE/language-server restart to pick it up immediately). The actual Release build was
never broken in this situation - it's purely the editor tooling looking at the wrong configuration's
generated output.

## Known gaps / next steps

- No dependency injection container yet - `AppServices` is a simple static holder for the shared
  `ForgeApiClient`/`ModDownloadService`. Worth swapping for `Microsoft.Extensions.DependencyInjection`
  + WPF UI's `INavigationService`/`IPageService` once the app grows past four pages.
- Downloads page fetches a version's archive but doesn't extract/install it anywhere - that's the
  TCFModSync integration point mentioned above.
- No SPT version picker yet (Updates/Dependencies pages take a free-text SPT version) - could be
  backed by `GetSptVersionsAsync`.
- No app icon.
