using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;
using static TCFModManager.Core.Tests.ModMetadataFixture;

namespace TCFModManager.Core.Tests;

public class AddonSharesTests : IDisposable
{
    private const string Folder = "7Bpencil.WeaponCamoAndStickers";
    private const string CamoGuid = "7bpencil.weaponcamoandstickers";
    private const int CamoId = 2658;
    private const int PrideId = 68;
    private const int EssentialId = 40;

    private readonly string _installRoot;
    private readonly string _plugins;
    private readonly string _camo;

    public AddonSharesTests()
    {
        _installRoot = Path.Combine(Path.GetTempPath(), "TCFModManagerTests_" + Guid.NewGuid());
        _plugins = Path.Combine(_installRoot, "BepInEx", "plugins");
        _camo = Path.Combine(_plugins, Folder);
        Directory.CreateDirectory(_camo);
    }

    public void Dispose()
    {
        if (Directory.Exists(_installRoot)) Directory.Delete(_installRoot, recursive: true);
    }

    [Fact]
    public void ParentAndTextureAddon_SplitIntoTwoEntries_ParentKeepsTheFolderName()
    {
        WriteCamo(_camo);
        var pride = WritePride(_camo);

        var mods = Scan(CamoRecord(), PrideRecord(pride));

        Assert.Equal(2, mods.Count);

        var camo = Assert.Single(mods, m => m is { ShareOwnerModId: CamoId, ShareOwnerIsAddon: false });
        Assert.Equal(Folder, camo.Name);
        Assert.Equal(CamoGuid, camo.Guid);
        Assert.Equal(["7Bpencil.WeaponCamoAndStickers.dll", "bundles/weapon-camo-and-stickers"], camo.OwnedFiles);

        var addon = Assert.Single(mods, m => m is { ShareOwnerModId: PrideId, ShareOwnerIsAddon: true });
        Assert.Equal("pride-flag-sticker-pack", addon.Name);
        Assert.Null(addon.Guid);
        Assert.Empty(addon.Assemblies);
        Assert.Equal(pride.Select(Relative).Order(StringComparer.Ordinal), addon.OwnedFiles);
        Assert.Equal(_camo, addon.FolderPath);
    }

    [Fact]
    public void TwoAddons_EveryFileHasExactlyOneOwner_AsOnThe41Install()
    {
        WriteCamo(_camo);
        var pride = WritePride(_camo);
        var essential = WriteEssential(_camo);

        var mods = Scan(CamoRecord(), PrideRecord(pride), EssentialRecord(essential));

        Assert.Equal(3, mods.Count);
        Assert.Equal(
            [Folder, "essential-texture-pack", "pride-flag-sticker-pack"],
            mods.Select(m => m.Name).Order(StringComparer.Ordinal));

        var owned = mods.SelectMany(m => m.OwnedFiles!).ToList();
        var onDisk = Directory.EnumerateFiles(_camo, "*", SearchOption.AllDirectories).ToList();
        Assert.Equal(onDisk.Count, owned.Count);
        Assert.Equal(owned.Count, owned.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void AddonPlacedBeforeItsParent_TheParentStillKeepsTheFolderName()
    {
        WriteCamo(_camo);
        var pride = WritePride(_camo);

        var mods = Scan(CamoRecord(placed: Day(2)), PrideRecord(pride, placed: Day(1)));

        Assert.Equal(Folder, Assert.Single(mods, m => !m.ShareOwnerIsAddon).Name);
        Assert.Equal("pride-flag-sticker-pack", Assert.Single(mods, m => m.ShareOwnerIsAddon).Name);
    }

    [Fact]
    public void AddonShippingADll_OwnsItByItsRecord_NotTheHost()
    {
        Directory.Delete(_camo);
        var folder = Path.Combine(_plugins, "BlackDiv");
        new ModMetadataFixture(Kind.Plain).Constructor(_ => { })
            .Plugin("com.wtt.blackdiv", "BlackDiv", "1.2.1")
            .Write(folder, "BlackDiv.dll");
        new ModMetadataFixture(Kind.Plain).Constructor(_ => { })
            .Plugin("com.someone.blackdivinfo", "BlackDivInfo", "1.1.0")
            .Write(folder, "BlackDivInfo.dll");

        var parent = Record(2511, false, "WTT - Black Division", "com.wtt.blackdiv", Day(1), "BepInEx/plugins/BlackDiv/BlackDiv.dll");
        var addon = Record(34, true, "Black Div spawn notifier", null, Day(2), "BepInEx/plugins/BlackDiv/BlackDivInfo.dll");
        addon.Slug = "black-div-spawn-notifier";

        var mods = InstalledModScanner.Scan(_installRoot, Owners(parent, addon));

        Assert.Equal(2, mods.Count);
        Assert.Equal(["BlackDiv.dll"], Assert.Single(mods, m => m.Name == "BlackDiv").OwnedFiles);

        var notifier = Assert.Single(mods, m => m.ShareOwnerIsAddon);
        Assert.Equal("black-div-spawn-notifier", notifier.Name);
        Assert.Equal(34, notifier.ShareOwnerModId);
        Assert.Equal(["BlackDivInfo.dll"], notifier.OwnedFiles);
    }

    [Fact]
    public void HandInstalledParent_BesideAnAppInstalledAddon_SplitsByTheParentsGuid()
    {
        WriteCamo(_camo);
        var pride = WritePride(_camo);

        var mods = InstalledModScanner.Scan(_installRoot, Owners([PrideRecord(pride)], (CamoGuid, CamoId)));

        Assert.Equal(2, mods.Count);

        var camo = Assert.Single(mods, m => !m.ShareOwnerIsAddon);
        Assert.Equal(Folder, camo.Name);
        Assert.Equal(CamoId, camo.ShareOwnerModId);
        Assert.Contains("bundles/weapon-camo-and-stickers", camo.OwnedFiles!);
    }

    [Fact]
    public void AddonAndModWithTheSameId_AreTwoShares_NotOne()
    {
        WriteCamo(_camo);
        var pride = WritePride(_camo);

        var parent = CamoRecord(id: 116);
        var addon = PrideRecord(pride, id: 116);

        var mods = Scan(parent, addon);

        Assert.Equal(2, mods.Count);
        Assert.Single(mods, m => m is { ShareOwnerModId: 116, ShareOwnerIsAddon: false, Name: Folder });
        Assert.Single(mods, m => m is { ShareOwnerModId: 116, ShareOwnerIsAddon: true, Name: "pride-flag-sticker-pack" });
    }

    [Fact]
    public void AddonRecordWithNoSlug_IsKeyedOnItsId()
    {
        WriteCamo(_camo);
        var pride = WritePride(_camo);
        var addon = PrideRecord(pride);
        addon.Slug = null;

        var mods = Scan(CamoRecord(), addon);

        Assert.Equal("addon-68", Assert.Single(mods, m => m.ShareOwnerIsAddon).Name);
    }

    [Fact]
    public void HandConfirmedAddonRecord_OwnsNothing_TheFolderIsOneEntry()
    {
        WriteCamo(_camo);
        var pride = WritePride(_camo);
        var confirmed = PrideRecord([]);
        var manual = new InstalledModRecord
        {
            ModId = confirmed.ModId, IsAddon = true, Name = confirmed.Name, Version = "1.0.0",
            InstalledAt = Day(2), IsAppManaged = false, Folders = [Folder],
        };

        var mod = Assert.Single(Scan(CamoRecord(), manual));

        Assert.Equal(Folder, mod.Name);
        Assert.False(mod.IsShare);
    }

    [Fact]
    public void SlugTakenByAnotherFolder_IsQualifiedWithTheFolderName()
    {
        WriteCamo(_camo);
        var pride = WritePride(_camo);
        new ModMetadataFixture(Kind.Plain).Constructor(_ => { })
            .Plugin("com.other.pride", "Pride", "1.0.0")
            .Write(Path.Combine(_plugins, "pride-flag-sticker-pack"), "Pride.dll");

        var mods = Scan(CamoRecord(), PrideRecord(pride));

        Assert.Equal($"{Folder}.pride-flag-sticker-pack", Assert.Single(mods, m => m.ShareOwnerIsAddon).Name);
        Assert.Single(mods, m => m is { Name: "pride-flag-sticker-pack", IsShare: false });
    }

    [Fact]
    public void DisabledAddonFiles_AreReportedAsTheAddonDisabled_BesideTheLiveParent()
    {
        WriteCamo(_camo);
        var off = Path.Combine(_installRoot, "BepInEx", "plugins.disabled", Folder);
        var pride = WritePride(off);

        var mods = Scan(CamoRecord(), PrideRecord(pride.Select(p => p.Replace("plugins.disabled", "plugins")).ToList()));

        Assert.Equal(2, mods.Count);
        Assert.False(Assert.Single(mods, m => !m.ShareOwnerIsAddon).IsDisabled);

        var addon = Assert.Single(mods, m => m.ShareOwnerIsAddon);
        Assert.True(addon.IsDisabled);
        Assert.Equal("pride-flag-sticker-pack", addon.Name);
        Assert.Equal(off, addon.FolderPath);
    }

    [Fact]
    public void OnlyAddonsClaimed_TheUnidentifiedFilesBecomeAHostThatKeepsTheFolderName()
    {
        new ModMetadataFixture(Kind.Plain).Constructor(_ => { }).Write(_camo, "Unlisted.dll");
        var pride = WritePride(_camo);
        var essential = WriteEssential(_camo);

        var mods = Scan(PrideRecord(pride), EssentialRecord(essential));

        Assert.Equal(3, mods.Count);

        var host = Assert.Single(mods, m => !m.ShareOwnerIsAddon);
        Assert.Equal(Folder, host.Name);
        Assert.Null(host.ShareOwnerModId);
        Assert.Equal(["Unlisted.dll"], host.OwnedFiles);
    }

    [Fact]
    public void FillAddonSlugs_FillsOnlyAddonRecordsWithoutOne_AndSavesOnce()
    {
        var path = Path.Combine(_installRoot, "installed-mods.json");
        var service = new ModInstallManifestService(path);
        var stale = PrideRecord([]);
        stale.Slug = null;
        var kept = EssentialRecord([]);
        kept.Slug = "kept-as-stored";
        service.Save(new ModInstallManifest { Mods = [CamoRecord(), stale, kept] });

        var filled = service.FillAddonSlugs(id => id switch
        {
            PrideId => "pride-flag-sticker-pack",
            EssentialId => "essential-texture-pack",
            _ => "should-not-be-asked",
        });

        Assert.Equal(1, filled);
        var manifest = service.Load();
        Assert.Equal("pride-flag-sticker-pack", manifest.Find(PrideId, true)!.Slug);
        Assert.Equal("kept-as-stored", manifest.Find(EssentialId, true)!.Slug);
        Assert.Null(manifest.Find(CamoId, false)!.Slug);
        Assert.Equal(0, service.FillAddonSlugs(_ => "again"));
    }

    [Fact]
    public void InstallTargetForAnAddon_CarriesItsSlug()
    {
        var target = InstallTarget.For(new Addon { Id = PrideId, Name = "Pride Flag Sticker Pack", Slug = "pride-flag-sticker-pack" });

        Assert.Equal("pride-flag-sticker-pack", target.Slug);
        Assert.True(target.IsAddon);
    }

    private List<InstalledMod> Scan(params InstalledModRecord[] records) =>
        InstalledModScanner.Scan(_installRoot, Owners(records));

    private static void WriteCamo(string folder)
    {
        new ModMetadataFixture(Kind.Plain).Constructor(_ => { })
            .Plugin(CamoGuid, "WeaponCamoAndStickers", "1.19.0")
            .Write(folder, "7Bpencil.WeaponCamoAndStickers.dll");
        Directory.CreateDirectory(Path.Combine(folder, "bundles"));
        File.WriteAllText(Path.Combine(folder, "bundles", "weapon-camo-and-stickers"), "bundle");
    }

    private List<string> WritePride(string folder) =>
        WriteFiles(folder, "textures/stickers/PrideStuff/Chevron/Bi.png", "textures/stickers/PrideStuff/Trans/Transgender.png");

    private List<string> WriteEssential(string folder) =>
        WriteFiles(folder, "presets/Essential-AKM-Berserk.json", "textures/camos/7Bpencil/pink.png", "textures/stickers/7Bpencil/neco-arc/dance-1.webm");

    private List<string> WriteFiles(string folder, params string[] relatives)
    {
        var placed = new List<string>();
        foreach (var relative in relatives)
        {
            var full = Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, relative);
            placed.Add(Path.GetRelativePath(_installRoot, full).Replace('\\', '/'));
        }

        return placed;
    }

    private string Relative(string installRelative) =>
        Path.GetRelativePath(_camo, Path.Combine(_installRoot, installRelative)).Replace('\\', '/');

    private InstalledModRecord CamoRecord(DateTimeOffset? placed = null, int id = CamoId) =>
        Record(id, false, "Camo And Stickers", CamoGuid, placed ?? Day(1),
            $"BepInEx/plugins/{Folder}/7Bpencil.WeaponCamoAndStickers.dll",
            $"BepInEx/plugins/{Folder}/bundles/weapon-camo-and-stickers");

    private InstalledModRecord PrideRecord(List<string> files, DateTimeOffset? placed = null, int id = PrideId)
    {
        var record = Record(id, true, "Pride Flag Sticker Pack", null, placed ?? Day(2), [.. files]);
        record.Slug = "pride-flag-sticker-pack";
        return record;
    }

    private InstalledModRecord EssentialRecord(List<string> files)
    {
        var record = Record(EssentialId, true, "Essential Texture Pack", null, Day(3), [.. files]);
        record.Slug = "essential-texture-pack";
        return record;
    }

    private InstalledModRecord Record(int id, bool isAddon, string name, string? guid, DateTimeOffset placed, params string[] files) =>
        new()
        {
            ModId = id,
            IsAddon = isAddon,
            Name = name,
            Guid = guid,
            Version = "1.0.0",
            InstalledAt = placed,
            Files = [.. files],
            InstallPath = InstallStamp.Of(_installRoot),
        };

    private static ModOwnership Owners(params InstalledModRecord[] records) => new(records);

    private static ModOwnership Owners(InstalledModRecord[] records, params (string Guid, int Id)[] listings) =>
        new(records, listings.Select(l => new KeyValuePair<string, int>(l.Guid, l.Id)));

    private static DateTimeOffset Day(int day) => new(2026, 10, day, 8, 0, 0, TimeSpan.Zero);
}
