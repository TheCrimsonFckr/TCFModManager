using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class InstalledModScannerTests : IDisposable
{
    private readonly string _installRoot;

    public InstalledModScannerTests()
    {
        _installRoot = Path.Combine(Path.GetTempPath(), "TCFModManagerTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_installRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_installRoot)) Directory.Delete(_installRoot, recursive: true);
    }

    [Fact]
    public void Scan_MissingInstallPath_ReturnsEmpty()
    {
        var result = InstalledModScanner.Scan(Path.Combine(_installRoot, "does-not-exist"));

        Assert.Empty(result);
    }

    [Fact]
    public void Scan_NullOrBlankInstallPath_ReturnsEmpty()
    {
        Assert.Empty(InstalledModScanner.Scan(null));
        Assert.Empty(InstalledModScanner.Scan("  "));
    }

    [Fact]
    public void Scan_NoModFoldersPresent_ReturnsEmpty()
    {
        // A real install always has BepInEx and user/mods, but nothing should throw if this
        // particular folder just happens not to (e.g. before the user has installed any mods yet).
        var result = InstalledModScanner.Scan(_installRoot);

        Assert.Empty(result);
    }

    [Fact]
    public void Scan_ServerMod_ReadsNameVersionAndStringAuthorFromPackageJson()
    {
        var modDir = Path.Combine(_installRoot, "user", "mods", "some-folder-name");
        Directory.CreateDirectory(modDir);
        File.WriteAllText(Path.Combine(modDir, "package.json"), """
            { "name": "SVM", "version": "2.1.0", "author": "Skwizzy" }
            """);

        var result = InstalledModScanner.Scan(_installRoot);

        var mod = Assert.Single(result);
        Assert.Equal("SVM", mod.Name);
        Assert.Equal("2.1.0", mod.Version);
        Assert.Equal("Skwizzy", mod.Author);
        Assert.Equal(InstalledModTarget.Server, mod.Target);
        Assert.Equal(modDir, mod.FolderPath);
    }

    [Fact]
    public void Scan_ServerMod_ReadsAuthorFromObjectForm()
    {
        // npm's package.json spec allows "author" to be an object with its own "name" field
        // instead of a plain string - both show up in the wild for SPT server mods.
        var modDir = Path.Combine(_installRoot, "user", "mods", "some-mod");
        Directory.CreateDirectory(modDir);
        File.WriteAllText(Path.Combine(modDir, "package.json"), """
            { "name": "SomeMod", "version": "1.0.0", "author": { "name": "SomeAuthor" } }
            """);

        var result = InstalledModScanner.Scan(_installRoot);

        var mod = Assert.Single(result);
        Assert.Equal("SomeAuthor", mod.Author);
    }

    [Fact]
    public void Scan_ServerMod_NoPackageJson_FallsBackToFolderNameWithNullVersion()
    {
        var modDir = Path.Combine(_installRoot, "SPT", "user", "mods", "mystery-mod");
        Directory.CreateDirectory(modDir);

        var result = InstalledModScanner.Scan(_installRoot);

        var mod = Assert.Single(result);
        Assert.Equal("mystery-mod", mod.Name);
        Assert.Null(mod.Version);
        Assert.Null(mod.Author);
    }

    [Fact]
    public void Scan_ServerMod_NoPackageJson_WithDll_AttemptsFallbackWithoutThrowing()
    {
        // Confirmed against a real install: some mods sit under user/mods as a bare client-style
        // DLL with no package.json at all (e.g. archangelwtf-lotsoflootredux, just a DLL + Config
        // folder) - the fallback should be attempted rather than leaving Version null outright. The
        // DLL content here isn't a real PE file, so this can't assert a real version was read, but
        // it does confirm the fallback path doesn't throw and the mod still shows up.
        var modDir = Path.Combine(_installRoot, "user", "mods", "dll-only-mod");
        Directory.CreateDirectory(modDir);
        File.WriteAllText(Path.Combine(modDir, "SomeMod.dll"), "not a real PE file");

        var result = InstalledModScanner.Scan(_installRoot);

        var mod = Assert.Single(result);
        Assert.Equal("dll-only-mod", mod.Name);
        Assert.Equal(InstalledModTarget.Server, mod.Target);
    }

    [Fact]
    public void Scan_ServerMod_PackageJsonVersion_NotOverriddenByDllFallback()
    {
        // The DLL fallback should only kick in when package.json didn't already provide a version -
        // a mod with both a valid manifest version and a stray DLL should still report the
        // manifest's version.
        var modDir = Path.Combine(_installRoot, "user", "mods", "hybrid-mod");
        Directory.CreateDirectory(modDir);
        File.WriteAllText(Path.Combine(modDir, "package.json"), """{ "name": "HybridMod", "version": "3.2.1" }""");
        File.WriteAllText(Path.Combine(modDir, "Companion.dll"), "not a real PE file");

        var result = InstalledModScanner.Scan(_installRoot);

        var mod = Assert.Single(result);
        Assert.Equal("3.2.1", mod.Version);
    }

    [Fact]
    public void Scan_ServerMod_MalformedPackageJson_FallsBackToFolderNameInsteadOfThrowing()
    {
        var modDir = Path.Combine(_installRoot, "user", "mods", "broken-mod");
        Directory.CreateDirectory(modDir);
        File.WriteAllText(Path.Combine(modDir, "package.json"), "{ not valid json");

        var result = InstalledModScanner.Scan(_installRoot);

        var mod = Assert.Single(result);
        Assert.Equal("broken-mod", mod.Name);
        Assert.Null(mod.Version);
    }

    [Fact]
    public void Scan_ChecksBothServerModsLayouts_WithoutDuplicatingWhenOnlyOneExists()
    {
        // SPT/user/mods (newer layout) and user/mods (older/root layout) are both checked, same
        // distinction SptInstallationService makes for the server exe - but a real install only
        // ever has one of them populated.
        var modDir = Path.Combine(_installRoot, "SPT", "user", "mods", "newer-layout-mod");
        Directory.CreateDirectory(modDir);

        var result = InstalledModScanner.Scan(_installRoot);

        var mod = Assert.Single(result);
        Assert.Equal("newer-layout-mod", mod.Name);
    }

    [Fact]
    public void Scan_ClientMod_FolderWithoutMatchingDll_ReportsNullVersion()
    {
        // A folder-based client mod where nothing inside is actually a valid/readable DLL -
        // shouldn't throw, and the mod should still be listed (just without a version).
        var modDir = Path.Combine(_installRoot, "BepInEx", "plugins", "SomeClientMod");
        Directory.CreateDirectory(modDir);
        File.WriteAllText(Path.Combine(modDir, "SomeClientMod.dll"), "not a real PE file");

        var result = InstalledModScanner.Scan(_installRoot);

        var mod = Assert.Single(result);
        Assert.Equal("SomeClientMod", mod.Name);
        Assert.Null(mod.Version);
        Assert.Equal(InstalledModTarget.Client, mod.Target);
    }

    [Fact]
    public void Scan_ClientMod_LooseDllDirectlyInPluginsFolder_IsDiscovered()
    {
        // Not every BepInEx plugin ships in its own subfolder - some are a single loose .dll
        // sitting directly in BepInEx/plugins.
        var pluginsDir = Path.Combine(_installRoot, "BepInEx", "plugins");
        Directory.CreateDirectory(pluginsDir);
        File.WriteAllText(Path.Combine(pluginsDir, "LooseMod.dll"), "not a real PE file");

        var result = InstalledModScanner.Scan(_installRoot);

        var mod = Assert.Single(result);
        Assert.Equal("LooseMod", mod.Name);
        Assert.Equal(InstalledModTarget.Client, mod.Target);
    }

    [Fact]
    public void Scan_ChecksBothBepInExPluginsAndPatchers()
    {
        Directory.CreateDirectory(Path.Combine(_installRoot, "BepInEx", "plugins", "PluginMod"));
        Directory.CreateDirectory(Path.Combine(_installRoot, "BepInEx", "patchers", "PatcherMod"));

        var result = InstalledModScanner.Scan(_installRoot);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, m => m.Name == "PluginMod");
        Assert.Contains(result, m => m.Name == "PatcherMod");
    }

    [Fact]
    public void Scan_FlagsPatcherFoldersAndLeavesPluginsUnflagged()
    {
        Directory.CreateDirectory(Path.Combine(_installRoot, "BepInEx", "plugins", "PluginMod"));
        Directory.CreateDirectory(Path.Combine(_installRoot, "BepInEx", "patchers", "PatcherMod"));

        var result = InstalledModScanner.Scan(_installRoot);

        Assert.False(Assert.Single(result, m => m.Name == "PluginMod").IsPatcher);
        Assert.True(Assert.Single(result, m => m.Name == "PatcherMod").IsPatcher);
    }

    [Fact]
    public void Scan_FlagsALoosePatcherDllDirectlyInTheContainer()
    {
        // Plenty of patchers ship as a single DLL rather than in a folder of their own.
        var patchersDir = Path.Combine(_installRoot, "BepInEx", "patchers");
        Directory.CreateDirectory(patchersDir);
        File.WriteAllText(Path.Combine(patchersDir, "SomePatcher.dll"), "not a real PE file");

        var mod = Assert.Single(InstalledModScanner.Scan(_installRoot));

        Assert.Equal("SomePatcher", mod.Name);
        Assert.True(mod.IsPatcher);
        Assert.Equal(InstalledModTarget.Client, mod.Target);
    }

    [Fact]
    public void Scan_FlagsPatchersInADisabledContainerToo()
    {
        Directory.CreateDirectory(Path.Combine(_installRoot, "BepInEx", "patchers.disabled", "ParkedPatcher"));

        var mod = Assert.Single(InstalledModScanner.Scan(_installRoot));

        Assert.True(mod.IsPatcher);
        Assert.True(mod.IsDisabled);
    }

    [Fact]
    public void Scan_SkipsPatcherFilesThatArentMods()
    {
        // SPT's own preloader patcher is part of the install rather than something the user added,
        // and FixPluginTypesSerialization is a general BepInEx utility mods bundle alongside
        // themselves. Neither is on sp-mod.com or the user's to manage, so listing them means
        // entries nothing can ever match, update or remove.
        var patchersDir = Path.Combine(_installRoot, "BepInEx", "patchers");
        Directory.CreateDirectory(patchersDir);
        File.WriteAllText(Path.Combine(patchersDir, "spt-prepatch.dll"), "not a real PE file");
        File.WriteAllText(Path.Combine(patchersDir, "aki-prepatch.dll"), "not a real PE file");
        File.WriteAllText(Path.Combine(patchersDir, "FixPluginTypesSerialization.dll"), "not a real PE file");
        File.WriteAllText(Path.Combine(patchersDir, "RealPatcher.dll"), "not a real PE file");

        var mod = Assert.Single(InstalledModScanner.Scan(_installRoot));

        Assert.Equal("RealPatcher", mod.Name);
    }

    [Fact]
    public void Scan_SkipsThoseFilesInADisabledPatchersContainerToo()
    {
        // Otherwise disabling the whole patchers folder makes them reappear as mods.
        var disabledDir = Path.Combine(_installRoot, "BepInEx", "patchers.disabled");
        Directory.CreateDirectory(disabledDir);
        File.WriteAllText(Path.Combine(disabledDir, "FixPluginTypesSerialization.dll"), "not a real PE file");

        Assert.Empty(InstalledModScanner.Scan(_installRoot));
    }

    [Fact]
    public void Scan_DoesNotSkipASptNamedPluginInThePluginsFolder()
    {
        // The patcher exclusion is scoped to BepInEx\patchers - a plugin that happens to carry one
        // of those names is still a mod.
        var pluginsDir = Path.Combine(_installRoot, "BepInEx", "plugins");
        Directory.CreateDirectory(pluginsDir);
        File.WriteAllText(Path.Combine(pluginsDir, "spt-prepatch.dll"), "not a real PE file");

        Assert.Single(InstalledModScanner.Scan(_installRoot));
    }

    [Fact]
    public void Scan_PopulatesInstalledAtFromFolderCreationTime()
    {
        var modDir = Path.Combine(_installRoot, "BepInEx", "plugins", "SomeClientMod");
        Directory.CreateDirectory(modDir);

        var result = InstalledModScanner.Scan(_installRoot);

        var mod = Assert.Single(result);
        Assert.NotNull(mod.InstalledAt);
        // Just created - should be within the last minute, not some far-off default value.
        Assert.True(DateTimeOffset.UtcNow - mod.InstalledAt < TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void Scan_CombinesClientAndServerMods()
    {
        Directory.CreateDirectory(Path.Combine(_installRoot, "BepInEx", "plugins", "ClientOne"));
        var serverDir = Path.Combine(_installRoot, "user", "mods", "ServerOne");
        Directory.CreateDirectory(serverDir);
        File.WriteAllText(Path.Combine(serverDir, "package.json"), """{ "name": "ServerOne", "version": "1.0.0" }""");

        var result = InstalledModScanner.Scan(_installRoot);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, m => m.Name == "ClientOne" && m.Target == InstalledModTarget.Client);
        Assert.Contains(result, m => m.Name == "ServerOne" && m.Target == InstalledModTarget.Server);
    }

    [Fact]
    public void Scan_ListsClientModsInADisabledContainer()
    {
        Directory.CreateDirectory(Path.Combine(_installRoot, "BepInEx", "plugins", "Live"));
        Directory.CreateDirectory(Path.Combine(_installRoot, "BepInEx", "plugins.disabled", "Parked"));

        var result = InstalledModScanner.Scan(_installRoot);

        Assert.Equal(2, result.Count);
        Assert.False(Assert.Single(result, m => m.Name == "Live").IsDisabled);
        Assert.True(Assert.Single(result, m => m.Name == "Parked").IsDisabled);
    }

    [Fact]
    public void Scan_ListsPatcherModsInADisabledContainer()
    {
        Directory.CreateDirectory(Path.Combine(_installRoot, "BepInEx", "patchers.disabled", "ParkedPatcher"));

        var result = InstalledModScanner.Scan(_installRoot);

        var mod = Assert.Single(result);
        Assert.Equal("ParkedPatcher", mod.Name);
        Assert.True(mod.IsDisabled);
        Assert.Equal(InstalledModTarget.Client, mod.Target);
    }

    [Fact]
    public void Scan_ListsServerModsInADisabledContainer()
    {
        var dir = Path.Combine(_installRoot, "user", "mods.disabled", "ParkedServer");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "package.json"), """{ "name": "ParkedServer", "version": "2.0.0" }""");

        var mod = Assert.Single(InstalledModScanner.Scan(_installRoot));

        Assert.Equal("ParkedServer", mod.Name);
        Assert.Equal("2.0.0", mod.Version);
        Assert.True(mod.IsDisabled);
    }

    [Fact]
    public void Scan_ListsTheSameModTwiceWhenItIsPresentInBothStates()
    {
        Directory.CreateDirectory(Path.Combine(_installRoot, "BepInEx", "plugins", "Split"));
        Directory.CreateDirectory(Path.Combine(_installRoot, "BepInEx", "plugins.disabled", "Split"));

        var result = InstalledModScanner.Scan(_installRoot);

        Assert.Equal(2, result.Count);
        Assert.Equal("Split", Assert.Single(ModDisableService.DuplicatedNames(result)));
    }

    [Fact]
    public void Scan_ReadsServerModDependenciesFromPackageJson()
    {
        var dir = Path.Combine(_installRoot, "user", "mods", "Consumer");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "package.json"), """
            { "name": "Consumer", "version": "1.0.0", "modDependencies": { "SharedTools": "1.0.0" } }
            """);

        var mod = Assert.Single(InstalledModScanner.Scan(_installRoot));

        var dependency = Assert.Single(mod.Dependencies);
        Assert.Equal("SharedTools", dependency.Identifier);
        Assert.False(dependency.IsSoft);
    }

    [Fact]
    public void Scan_ReadsServerModDependenciesWrittenAsAnArray()
    {
        var dir = Path.Combine(_installRoot, "user", "mods", "Consumer");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "package.json"), """
            { "name": "Consumer", "version": "1.0.0", "modDependencies": ["SharedTools"] }
            """);

        var mod = Assert.Single(InstalledModScanner.Scan(_installRoot));

        Assert.Equal("SharedTools", Assert.Single(mod.Dependencies).Identifier);
    }

    [Fact]
    public void Scan_ModWithNoDeclaredDependencies_ReportsNone()
    {
        Directory.CreateDirectory(Path.Combine(_installRoot, "BepInEx", "plugins", "Plain"));

        Assert.Empty(Assert.Single(InstalledModScanner.Scan(_installRoot)).Dependencies);
    }
}
