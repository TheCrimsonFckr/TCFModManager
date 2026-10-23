using System.Net;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;
using static TCFModManager.Core.Tests.ModMetadataFixture;

namespace TCFModManager.Core.Tests;

public class AddonActionsTests : IDisposable
{
    private const string Folder = "7Bpencil.WeaponCamoAndStickers";
    private const string CamoGuid = "7bpencil.weaponcamoandstickers";
    private const int CamoId = 2658;
    private const int PrideId = 68;
    private const int EssentialId = 40;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "tcfmm-addons-" + Guid.NewGuid().ToString("N"));
    private readonly string _install;
    private readonly string _live;
    private readonly string _off;
    private readonly ModInstallManifestService _manifest;
    private readonly InstalledModRecord _camo;
    private readonly InstalledModRecord _pride;
    private readonly InstalledModRecord _essential;

    public AddonActionsTests()
    {
        _install = Path.Combine(_root, "install");
        _live = Path.Combine(_install, "BepInEx", "plugins", Folder);
        _off = Path.Combine(_install, "BepInEx", "plugins.disabled", Folder);
        Directory.CreateDirectory(_install);
        File.WriteAllText(Path.Combine(_install, "EscapeFromTarkov.exe"), "game");
        _manifest = new ModInstallManifestService(Path.Combine(_root, "installed-mods.json"));

        new ModMetadataFixture(Kind.Plain).Constructor(_ => { })
            .Plugin(CamoGuid, "WeaponCamoAndStickers", "1.19.0")
            .Write(_live, "7Bpencil.WeaponCamoAndStickers.dll");

        _camo = Record(CamoId, false, "Camo And Stickers", CamoGuid, Day(1), null,
            [$"BepInEx/plugins/{Folder}/7Bpencil.WeaponCamoAndStickers.dll", .. Write("bundles/weapon-camo-and-stickers")]);
        _pride = Record(PrideId, true, "Pride Flag Sticker Pack", null, Day(2), "pride-flag-sticker-pack",
            Write("textures/stickers/PrideStuff/Chevron/Bi.png", "textures/stickers/PrideStuff/Trans/Transgender.png"));
        _essential = Record(EssentialId, true, "Essential Texture Pack", null, Day(3), "essential-texture-pack",
            Write("presets/Essential-AKM-Berserk.json", "textures/stickers/7Bpencil/eft/bear.png"));

        _manifest.Save(new ModInstallManifest { Mods = [_camo, _pride, _essential] });
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    [Fact]
    public async Task RemovingAParentWithItsAddons_IsOneBatch_AndOneUndoPutsEverythingBack()
    {
        var service = Service();
        var batch = RemovalBatch.For(_camo.Name);

        await service.UninstallAsync(_install, _pride, batch: batch);
        await service.UninstallAsync(_install, _essential, batch: batch);
        await service.UninstallAsync(_install, _camo, batch: batch);

        Assert.False(Directory.Exists(_live));
        Assert.Empty(_manifest.Load().Mods);

        var held = RemovedMods.Undoable(_install);
        Assert.Equal(3, held.Count);
        Assert.All(held, h => Assert.Equal(batch.Id, h.Log.BatchId));

        var addonFolder = held.Single(h => h.Log.ModName == _pride.Name).Folder;
        var together = RemovedMods.BatchOf(_install, addonFolder);
        Assert.Equal(3, together.Count);
        Assert.Equal(_camo.Name, together[0].Log.ModName);
        Assert.True(together[0].Log.IsBatchLead);

        var undo = service.UndoRemovalBatch(_install, addonFolder);

        Assert.True(undo.Ran);
        Assert.Empty(undo.Blocked);
        Assert.Empty(RemovedMods.Undoable(_install));
        Assert.All(_camo.Files.Concat(_pride.Files).Concat(_essential.Files), f => Assert.True(File.Exists(Full(f)), f));
        Assert.Equal(3, _manifest.Load().Mods.Count);
    }

    [Fact]
    public async Task RemovingOneAddon_LeavesItsParentAndTheOtherAddon_AndTidiesOnlyItsOwnFolders()
    {
        await Service().UninstallAsync(_install, _pride);

        Assert.All(_pride.Files, f => Assert.False(File.Exists(Full(f)), f));
        Assert.False(Directory.Exists(Path.Combine(_live, "textures", "stickers", "PrideStuff")));
        Assert.All(_camo.Files.Concat(_essential.Files), f => Assert.True(File.Exists(Full(f)), f));
        Assert.Null(_manifest.Load().Find(PrideId, true));
        Assert.NotNull(_manifest.Load().Find(CamoId, false));
    }

    [Fact]
    public async Task UndoRemovalBatch_OfARemovalOutsideAnyBatch_UndoesJustThatOne()
    {
        var service = Service();
        await service.UninstallAsync(_install, _pride);
        await service.UninstallAsync(_install, _essential);

        var prideFolder = RemovedMods.Undoable(_install).Single(h => h.Log.ModName == _pride.Name).Folder;
        var undo = service.UndoRemovalBatch(_install, prideFolder);

        Assert.True(undo.Ran);
        Assert.All(_pride.Files, f => Assert.True(File.Exists(Full(f)), f));
        Assert.All(_essential.Files, f => Assert.False(File.Exists(Full(f)), f));
        Assert.Single(RemovedMods.Undoable(_install));
    }

    [Fact]
    public void DisablingAnAddonAlone_MovesOnlyItsFiles()
    {
        var pride = Assert.Single(Scan(), m => m is { ShareOwnerIsAddon: true, ShareOwnerModId: PrideId });

        var outcome = ModDisableService.Apply([pride], disable: true, _install);

        Assert.Empty(outcome.Failed);
        Assert.True(File.Exists(Path.Combine(_off, "textures", "stickers", "PrideStuff", "Trans", "Transgender.png")));
        Assert.False(Directory.Exists(Path.Combine(_live, "textures", "stickers", "PrideStuff")));
        Assert.All(_camo.Files.Concat(_essential.Files), f => Assert.True(File.Exists(Full(f)), f));

        var after = Scan();
        var disabled = Assert.Single(after, m => m is { ShareOwnerIsAddon: true, ShareOwnerModId: PrideId });
        Assert.True(disabled.IsDisabled);
        Assert.Equal("pride-flag-sticker-pack", disabled.Name);
        Assert.False(Assert.Single(after, m => !m.ShareOwnerIsAddon).IsDisabled);
    }

    [Fact]
    public void DisablingAParentWithItsAddons_MovesTheWholeFolder_AndEnablingBringsItAllBack()
    {
        var scanned = Scan();
        var parent = Assert.Single(scanned, m => !m.ShareOwnerIsAddon);
        var addons = scanned.Where(m => m.ShareOwnerIsAddon).ToList();
        Assert.Equal(2, addons.Count);

        var off = ModDisableService.Apply([parent, .. addons], disable: true, _install);

        Assert.Empty(off.Failed);
        Assert.False(Directory.Exists(_live));
        Assert.All(Scan(), m => Assert.True(m.IsDisabled, m.Name));

        var on = ModDisableService.Apply(Scan(), disable: false, _install);

        Assert.Empty(on.Failed);
        Assert.False(Directory.Exists(_off));
        Assert.All(_camo.Files.Concat(_pride.Files).Concat(_essential.Files), f => Assert.True(File.Exists(Full(f)), f));
        Assert.Equal(3, Scan().Count(m => !m.IsDisabled));
    }

    [Fact]
    public void DisablingAParentWithItsAddons_RevertPutsEverythingBack()
    {
        var scanned = Scan();
        var outcome = ModDisableService.Apply(scanned, disable: true, _install);

        var revert = ModDisableService.Revert(outcome.Moved, _install);

        Assert.Empty(revert.Failed);
        Assert.False(Directory.Exists(_off));
        Assert.All(_camo.Files.Concat(_pride.Files).Concat(_essential.Files), f => Assert.True(File.Exists(Full(f)), f));
    }

    [Fact]
    public void DisabledWithParent_IsMarkedAndClearedPerAddon()
    {
        Assert.Equal(1, _manifest.SetDisabledWithParent([PrideId], true));
        Assert.Equal(0, _manifest.SetDisabledWithParent([PrideId], true));

        Assert.Equal([PrideId], _manifest.Load().Mods.Where(r => r.DisabledWithParent).Select(r => r.ModId));

        Assert.Equal(1, _manifest.SetDisabledWithParent([PrideId, EssentialId], false));
        Assert.DoesNotContain(_manifest.Load().Mods, r => r.DisabledWithParent);
    }

    [Fact]
    public void DisabledForFit_IsMarkedAndClearedPerAddon_AndLeftOutWhileFalse()
    {
        Assert.Equal(1, _manifest.SetDisabledForFit([PrideId], true));
        Assert.Equal(0, _manifest.SetDisabledForFit([PrideId], true));
        Assert.Equal([PrideId], _manifest.Load().Mods.Where(r => r.DisabledForFit).Select(r => r.ModId));
        Assert.DoesNotContain(_manifest.Load().Mods, r => r.DisabledWithParent);

        Assert.Equal(1, _manifest.SetDisabledForFit([PrideId, EssentialId], false));
        Assert.DoesNotContain("\"DisabledForFit\"", File.ReadAllText(_manifest.FilePath));
    }

    [Fact]
    public void DisabledWithParent_IsLeftOutOfTheManifestWhileFalse()
    {
        _manifest.SetDisabledWithParent([PrideId], true);
        var json = File.ReadAllText(_manifest.FilePath);

        Assert.Single(System.Text.RegularExpressions.Regex.Matches(json, "\"DisabledWithParent\""));
    }

    private List<InstalledMod> Scan() =>
        InstalledModScanner.Scan(_install, new ModOwnership(_manifest.Load().Mods));

    private string[] Write(params string[] relatives)
    {
        foreach (var relative in relatives)
        {
            var full = Path.Combine(_live, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, relative);
        }

        return [.. relatives.Select(r => $"BepInEx/plugins/{Folder}/{r}")];
    }

    private string Full(string relative) => Path.Combine(_install, relative.Replace('/', Path.DirectorySeparatorChar));

    private InstalledModRecord Record(
        int id, bool isAddon, string name, string? guid, DateTimeOffset placed, string? slug, string[] files) => new()
    {
        ModId = id,
        IsAddon = isAddon,
        Name = name,
        Guid = guid,
        Version = "1.0.0",
        InstalledAt = placed,
        Files = [.. files],
        Folders = [Folder],
        InstallPath = InstallStamp.Of(_install),
        Slug = slug,
    };

    private ModInstallService Service() => new(
        new ModDownloadService(new HttpClient(new NoNetwork())),
        _manifest,
        removedMods: new RemovedMods(() => RemovedModsRetention.UntilCleared));

    private static DateTimeOffset Day(int day) => new(2026, 10, day, 8, 0, 0, TimeSpan.Zero);

    private sealed class NoNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
