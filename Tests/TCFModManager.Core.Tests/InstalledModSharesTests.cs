using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;
using static TCFModManager.Core.Tests.ModMetadataFixture;

namespace TCFModManager.Core.Tests;

public class InstalledModSharesTests : IDisposable
{
    private const string StanceGuid = "com.fontaine.spt-stance-overhaul";
    private const string LibGuid = "com.fontaine.spt-realism-common-lib";
    private const string RealismGuid = "RealismMod";

    private readonly string _installRoot;
    private readonly string _plugins;

    public InstalledModSharesTests()
    {
        _installRoot = Path.Combine(Path.GetTempPath(), "TCFModManagerTests_" + Guid.NewGuid());
        _plugins = Path.Combine(_installRoot, "BepInEx", "plugins");
        Directory.CreateDirectory(_plugins);
    }

    public void Dispose()
    {
        if (Directory.Exists(_installRoot)) Directory.Delete(_installRoot, recursive: true);
    }

    [Fact]
    public void AppInstalledPair_SplitsIntoOneEntryPerMod_FirstPlacedKeepsTheFolderName()
    {
        WriteStance(Path.Combine(_plugins, "Realism"));
        WriteLib(Path.Combine(_plugins, "Realism"));

        var mods = InstalledModScanner.Scan(_installRoot, Owners(StanceRecord(), LibRecord()));

        Assert.Equal(2, mods.Count);

        var stance = Assert.Single(mods, m => m.ShareOwnerModId == 3136);
        Assert.Equal("Realism", stance.Name);
        Assert.Equal(StanceGuid, stance.Guid);
        Assert.Equal("0.0.2", stance.Version);
        Assert.Equal(["curves.json", "spt-stance-overhaul.dll"], stance.OwnedFiles);
        Assert.Equal(["spt-stance-overhaul.dll"], stance.Assemblies.Select(a => a.RelativePath));
        Assert.Contains(stance.Dependencies, d => d.Identifier == LibGuid);
        Assert.Equal(Path.Combine(_plugins, "Realism"), stance.FolderPath);

        var lib = Assert.Single(mods, m => m.ShareOwnerModId == 3135);
        Assert.Equal("spt-realism-common-lib", lib.Name);
        Assert.Equal(LibGuid, lib.Guid);
        Assert.Equal([LibGuid], lib.AllGuids);
        Assert.Equal(["spt-realism-common-lib.dll"], lib.OwnedFiles);
        Assert.Empty(lib.Dependencies);
    }

    [Fact]
    public void NoOwnership_ReportsTheFolderAsOneEntry_AsBefore()
    {
        WriteStance(Path.Combine(_plugins, "Realism"));
        WriteLib(Path.Combine(_plugins, "Realism"));

        var mod = Assert.Single(InstalledModScanner.Scan(_installRoot));

        Assert.Equal("Realism", mod.Name);
        Assert.False(mod.IsShare);
        Assert.Null(mod.ShareOwnerModId);
        Assert.Equal(2, mod.AllGuids.Count);
    }

    [Fact]
    public void FolderWithOneOwner_IsNotSplit()
    {
        WriteLib(Path.Combine(_plugins, "Realism"));

        var mod = Assert.Single(InstalledModScanner.Scan(_installRoot, Owners(StanceRecord(), LibRecord())));

        Assert.Equal("Realism", mod.Name);
        Assert.False(mod.IsShare);
    }

    [Fact]
    public void LaterArrival_DoesNotRenameTheModAlreadyThere()
    {
        WriteLib(Path.Combine(_plugins, "Realism"));
        var lib = LibRecord(placed: Day(1));

        var before = Assert.Single(InstalledModScanner.Scan(_installRoot, Owners(lib)));

        WriteStance(Path.Combine(_plugins, "Realism"));
        var after = InstalledModScanner.Scan(_installRoot, Owners(lib, StanceRecord(placed: Day(2))));

        Assert.Equal("Realism", before.Name);
        Assert.Equal("Realism", Assert.Single(after, m => m.ShareOwnerModId == 3135).Name);
        Assert.Equal("spt-stance-overhaul", Assert.Single(after, m => m.ShareOwnerModId == 3136).Name);
    }

    [Fact]
    public void HandInstalledPair_SplitsByListingGuid()
    {
        WriteStance(Path.Combine(_plugins, "Realism"));
        WriteLib(Path.Combine(_plugins, "Realism"));

        var mods = InstalledModScanner.Scan(_installRoot, Owners([], (StanceGuid, 3136), (LibGuid, 3135)));

        Assert.Equal(2, mods.Count);
        Assert.All(mods, m => Assert.True(m.IsShare));
        Assert.Equal([3135, 3136], mods.Select(m => m.ShareOwnerModId!.Value).Order());
        Assert.Single(mods, m => m.Name == "Realism");
    }

    [Fact]
    public void HandInstalledRealism_BesideAppInstalledPair_GetsItsOwnEntry()
    {
        var folder = Path.Combine(_plugins, "Realism");
        WriteStance(folder);
        WriteLib(folder);
        new ModMetadataFixture(Kind.Plain).Constructor(_ => { })
            .Plugin(RealismGuid, "SPTRealism", "1.6.4")
            .Dependency(LibGuid, flags: 1)
            .Write(folder, "RealismMod.dll");
        File.WriteAllText(Path.Combine(folder, "config.json"), "{}");

        var mods = InstalledModScanner.Scan(_installRoot, Owners(StanceRecord(), LibRecord()));

        Assert.Equal(3, mods.Count);

        var realism = Assert.Single(mods, m => m.ShareOwnerModId is null);
        Assert.Equal("RealismMod", realism.Name);
        Assert.Equal(RealismGuid, realism.Guid);
        Assert.Equal("1.6.4", realism.Version);
        Assert.Equal(["RealismMod.dll", "config.json"], realism.OwnedFiles);

        Assert.Equal("Realism", Assert.Single(mods, m => m.ShareOwnerModId == 3136).Name);
        Assert.Equal("spt-realism-common-lib", Assert.Single(mods, m => m.ShareOwnerModId == 3135).Name);
    }

    [Fact]
    public void HandInstalledModWithAnUnlistedSecondPlugin_IsNotSplit()
    {
        var folder = Path.Combine(_plugins, "MainMod");
        new ModMetadataFixture(Kind.Plain).Constructor(_ => { })
            .Plugin("com.example.main", "Main", "1.0.0").Write(folder, "MainMod.dll");
        new ModMetadataFixture(Kind.Plain).Constructor(_ => { })
            .Plugin("com.example.main.api", "Main API", "1.0.0").Write(folder, "MainMod.Api.dll");

        var mod = Assert.Single(InstalledModScanner.Scan(_installRoot, Owners([], ("com.example.main", 10))));

        Assert.Equal("MainMod", mod.Name);
        Assert.False(mod.IsShare);
    }

    [Fact]
    public void ListedLibraryTheRecordPlacedItself_StaysWithThatMod()
    {
        var folder = Path.Combine(_plugins, "BigMod");
        new ModMetadataFixture(Kind.Plain).Constructor(_ => { })
            .Plugin("com.example.big", "Big", "2.0.0").Write(folder, "BigMod.dll");
        WriteLib(folder);

        var record = Record(50, "Big Mod", "com.example.big", Day(1),
            "BepInEx/plugins/BigMod/BigMod.dll", "BepInEx/plugins/BigMod/spt-realism-common-lib.dll");

        var mod = Assert.Single(InstalledModScanner.Scan(_installRoot, Owners([record], (LibGuid, 3135), ("com.example.big", 50))));

        Assert.Equal("BigMod", mod.Name);
        Assert.False(mod.IsShare);
    }

    [Fact]
    public void DisabledShare_KeepsItsNameAndOwner()
    {
        WriteLib(Path.Combine(_plugins, "Realism"));
        WriteStance(Path.Combine(_installRoot, "BepInEx", "plugins.disabled", "Realism"));

        var mods = InstalledModScanner.Scan(_installRoot, Owners(StanceRecord(), LibRecord()));

        var stance = Assert.Single(mods, m => m.ShareOwnerModId == 3136);
        Assert.True(stance.IsDisabled);
        Assert.Equal("Realism", stance.Name);
        Assert.Empty(stance.Assemblies);
        Assert.Equal(Path.Combine(_installRoot, "BepInEx", "plugins.disabled", "Realism"), stance.FolderPath);

        var lib = Assert.Single(mods, m => m.ShareOwnerModId == 3135);
        Assert.False(lib.IsDisabled);
        Assert.Equal("spt-realism-common-lib", lib.Name);
    }

    [Fact]
    public void RecordsFromAnotherInstall_AreIgnored()
    {
        WriteStance(Path.Combine(_plugins, "Realism"));
        WriteLib(Path.Combine(_plugins, "Realism"));
        var elsewhere = Path.Combine(Path.GetTempPath(), "SomeOtherInstall");

        var mod = Assert.Single(InstalledModScanner.Scan(_installRoot,
            Owners(StanceRecord(installPath: elsewhere), LibRecord(installPath: elsewhere))));

        Assert.False(mod.IsShare);
    }

    [Fact]
    public void ShareNameTakenByAnotherEntry_IsQualifiedWithTheFolder()
    {
        WriteStance(Path.Combine(_plugins, "Realism"));
        WriteLib(Path.Combine(_plugins, "Realism"));
        new ModMetadataFixture(Kind.Plain).Constructor(_ => { })
            .Plugin("com.example.other", "Other", "1.0.0")
            .Write(_plugins, "spt-realism-common-lib.dll");

        var mods = InstalledModScanner.Scan(_installRoot, Owners(StanceRecord(), LibRecord()));

        Assert.Equal("Realism.spt-realism-common-lib", Assert.Single(mods, m => m.ShareOwnerModId == 3135).Name);
        Assert.Single(mods, m => m.Name == "spt-realism-common-lib" && !m.IsShare);
    }

    private void WriteStance(string folder)
    {
        new ModMetadataFixture(Kind.Plain).Constructor(_ => { })
            .Plugin(StanceGuid, "Fontaine-SPTStanceOverhaul", "0.0.2")
            .Dependency(LibGuid, flags: 1)
            .Write(folder, "spt-stance-overhaul.dll");
        File.WriteAllText(Path.Combine(folder, "curves.json"), "{}");
    }

    private static void WriteLib(string folder) =>
        new ModMetadataFixture(Kind.Plain).Constructor(_ => { })
            .Plugin(LibGuid, "Fontaine-RealismCommonLib", "0.0.2")
            .Write(folder, "spt-realism-common-lib.dll");

    private InstalledModRecord StanceRecord(DateTimeOffset? placed = null, string? installPath = null) =>
        Stamped(Record(3136, "SPT Stance Overhaul", StanceGuid, placed ?? Day(1),
            "BepInEx/plugins/Realism/curves.json", "BepInEx/plugins/Realism/spt-stance-overhaul.dll"), installPath);

    private InstalledModRecord LibRecord(DateTimeOffset? placed = null, string? installPath = null) =>
        Stamped(Record(3135, "SPT Realism Common Lib", LibGuid, placed ?? Day(1).AddSeconds(1),
            "BepInEx/plugins/Realism/spt-realism-common-lib.dll"), installPath);

    private static InstalledModRecord Stamped(InstalledModRecord record, string? installPath)
    {
        if (installPath is not null) record.InstallPath = InstallStamp.Of(installPath);
        return record;
    }

    private InstalledModRecord Record(int id, string name, string guid, DateTimeOffset placed, params string[] files) =>
        new()
        {
            ModId = id,
            Name = name,
            Guid = guid,
            Version = "0.0.2",
            InstalledAt = placed,
            Files = [.. files],
            InstallPath = InstallStamp.Of(_installRoot),
        };

    private static ModOwnership Owners(params InstalledModRecord[] records) => new(records);

    private static ModOwnership Owners(InstalledModRecord[] records, params (string Guid, int Id)[] listings) =>
        new(records, listings.Select(l => new KeyValuePair<string, int>(l.Guid, l.Id)));

    private static DateTimeOffset Day(int day) => new(2026, 10, day, 8, 0, 0, TimeSpan.Zero);
}
