using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

//
// An install is simulated at the seam ConfigCarryOver actually sits on: Prepare, then the placement
// (writing the new version's files over the install), then Settle. Nothing here downloads anything.
//
public class ConfigCarryOverTests : IDisposable
{
    private static readonly DateTimeOffset Timestamp = new(2026, 9, 12, 10, 0, 0, TimeSpan.Zero);

    private const string ConfigPath = "user/mods/TestMod/config.json";

    private readonly string _root;
    private readonly string _install;
    private readonly string _archiveRoot;
    private readonly ConfigBaselineStore _baselines;
    private readonly ModConfigOptionsStore _options;
    private readonly ConfigCarryOver _carry;

    public ConfigCarryOverTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "TCFModManagerCarryOver_" + Guid.NewGuid());
        _install = Path.Combine(_root, "install");
        _archiveRoot = Path.Combine(_root, "LegacyConfigs");
        Directory.CreateDirectory(_install);

        _baselines = new ConfigBaselineStore(Path.Combine(_root, "ConfigBaselines"));
        _options = new ModConfigOptionsStore(Path.Combine(_root, "mod_configs.json"));
        _carry = new ConfigCarryOver(_baselines, _archiveRoot, _options);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private void Write(string relative, string text)
    {
        var path = Path.Combine(_install, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private string Read(string relative) =>
        File.ReadAllText(Path.Combine(_install, relative.Replace('/', Path.DirectorySeparatorChar)));

    private static InstalledModRecord Record(string version, params string[] files) => new()
    {
        ModId = 42,
        Name = "Test Mod",
        Version = version,
        InstalledAt = Timestamp,
        Files = [.. files],
        Folders = InstalledModFolders.FromPlacedFiles(files),
    };

    private static InstallTarget Target() => new(42, IsAddon: false, "Test Mod", null, null, null);

    private ConfigFileOutcome Only(ConfigUpdateReport report, string path) =>
        Assert.Single(report.Files, f => f.Path == path);

    //
    // The whole update, as the install path runs it: copy aside, place the new file, settle.
    //
    private ConfigUpdateReport Update(InstalledModRecord? existing, string newVersion, params (string Path, string Text)[] shipped)
    {
        var pending = _carry.Prepare(_install, existing, shipped.Select(s => s.Path), "Test Mod", Timestamp);

        foreach (var (path, text) in shipped)
            if (!pending.Untouchable.Contains(path)) Write(path, text);

        var installed = Record(newVersion, [.. shipped.Select(s => s.Path)]);

        return _carry.Settle(pending, _install, Target(), existing, installed, Timestamp);
    }

    [Fact]
    public void AFirstInstallReportsTheConfigAsAdded()
    {
        var report = Update(existing: null, "1.0.0", (ConfigPath, "{ \"a\": 1 }"));

        Assert.Equal(ConfigOutcomeKind.Added, Only(report, ConfigPath).Kind);
        Assert.Null(report.ArchiveFolder);
        Assert.NotNull(_baselines.Find(42, false, "1.0.0", ConfigPath));
    }

    //
    // The first update after this ships: there is no record of what the old version put there, so the
    // honest answer is that the file was replaced, and the reason says why it could not do better.
    //
    [Fact]
    public void WithNoBaselineTheFileIsReportedAsReplaced()
    {
        Write(ConfigPath, "{ \"a\": 99 }");

        var report = Update(Record("1.0.0", ConfigPath), "1.1.0", (ConfigPath, "{ \"a\": 1 }"));

        var outcome = Only(report, ConfigPath);
        Assert.Equal(ConfigOutcomeKind.Replaced, outcome.Kind);
        Assert.Equal(ConfigReplaceReason.NoBaseline, outcome.Reason);
        Assert.Equal("{ \"a\": 1 }", Read(ConfigPath));

        // The user's copy is the thing that has to survive.
        var archived = Path.Combine(report.ArchiveFolder!, ConfigPath.Replace('/', Path.DirectorySeparatorChar));
        Assert.Equal("{ \"a\": 99 }", File.ReadAllText(archived));
    }

    [Fact]
    public void AFileTheUserNeverTouchedIsReportedAsDefaultsUpdated()
    {
        Write(ConfigPath, "{ \"a\": 1 }");
        _baselines.Capture(_install, 42, false, "1.0.0", [ConfigPath]);

        var report = Update(Record("1.0.0", ConfigPath), "1.1.0", (ConfigPath, "{ \"a\": 2 }"));

        Assert.Equal(ConfigOutcomeKind.DefaultsUpdated, Only(report, ConfigPath).Kind);
    }

    [Fact]
    public void AnEditedFileWithABaselineIsReplacedWithNoReason()
    {
        Write(ConfigPath, "{ \"a\": 1 }");
        _baselines.Capture(_install, 42, false, "1.0.0", [ConfigPath]);
        Write(ConfigPath, "{ \"a\": 99 }");

        var report = Update(Record("1.0.0", ConfigPath), "1.1.0", (ConfigPath, "{ \"a\": 2 }"));

        var outcome = Only(report, ConfigPath);
        Assert.Equal(ConfigOutcomeKind.Replaced, outcome.Kind);
        Assert.Null(outcome.Reason);
    }

    [Fact]
    public void AnIdenticalFileIsReportedAsUnchanged()
    {
        Write(ConfigPath, "{ \"a\": 1 }");

        var report = Update(Record("1.0.0", ConfigPath), "1.1.0", (ConfigPath, "{ \"a\": 1 }"));

        Assert.Equal(ConfigOutcomeKind.Unchanged, Only(report, ConfigPath).Kind);
    }

    //
    // A mod installed by hand has no record, so nothing used to be copied aside and the new archive
    // simply overwrote whatever was there.
    //
    [Fact]
    public void AnUntrackedConfigIsStillCopiedAsideBeforeItIsOverwritten()
    {
        Write(ConfigPath, "{ \"mine\": true }");

        var report = Update(existing: null, "1.1.0", (ConfigPath, "{ \"mine\": false }"));

        var archived = Path.Combine(report.ArchiveFolder!, ConfigPath.Replace('/', Path.DirectorySeparatorChar));
        Assert.Equal("{ \"mine\": true }", File.ReadAllText(archived));
        Assert.Equal(ConfigOutcomeKind.Replaced, Only(report, ConfigPath).Kind);
    }

    [Fact]
    public void AConfigTheNewVersionNoLongerShipsIsReportedAsRemoved()
    {
        Write(ConfigPath, "{ \"a\": 1 }");
        Write("user/mods/TestMod/config/extra.json", "{ \"b\": 2 }");

        var report = Update(
            Record("1.0.0", ConfigPath, "user/mods/TestMod/config/extra.json"),
            "1.1.0",
            (ConfigPath, "{ \"a\": 1 }"));

        Assert.Equal(ConfigOutcomeKind.Removed, Only(report, "user/mods/TestMod/config/extra.json").Kind);
    }

    //
    // Nothing else in the mod's folder is any of this feature's business.
    //
    [Fact]
    public void AFileThatIsNotAConfigIsNotReportedAtAll()
    {
        Write("user/mods/TestMod/package.json", "{ \"name\": \"test\" }");

        var report = Update(existing: null, "1.0.0", ("user/mods/TestMod/package.json", "{ \"name\": \"test\" }"));

        Assert.Empty(report.Files);
    }

    //
    // A file that cannot be copied aside is left exactly as it was: the caller skips placing over it,
    // which is what the install path does with Untouchable.
    //
    [Fact]
    public void AConfigThatCannotBeCopiedAsideIsLeftAlone()
    {
        Write(ConfigPath, "{ \"mine\": true }");

        // A file sitting where the archive folder needs to be makes the copy fail.
        Directory.CreateDirectory(_archiveRoot);
        File.WriteAllText(ModConfigFiles.ArchiveFolder(_archiveRoot, "Test Mod", Timestamp), "in the way");

        var report = Update(Record("1.0.0", ConfigPath), "1.1.0", (ConfigPath, "{ \"mine\": false }"));

        Assert.Equal(ConfigOutcomeKind.NotUpdated, Only(report, ConfigPath).Kind);
        Assert.Equal("{ \"mine\": true }", Read(ConfigPath));
    }

    [Fact]
    public void SettleKeepsTheTwoNewestBaselinesAndPrunesTheRest()
    {
        Write(ConfigPath, "{ \"a\": 1 }");
        _baselines.Capture(_install, 42, false, "0.9.0", [ConfigPath]);
        _baselines.Capture(_install, 42, false, "1.0.0", [ConfigPath]);

        Update(Record("1.0.0", ConfigPath), "1.1.0", (ConfigPath, "{ \"a\": 2 }"));

        Assert.Null(_baselines.Find(42, false, "0.9.0", ConfigPath));
        Assert.NotNull(_baselines.Find(42, false, "1.0.0", ConfigPath));
        Assert.NotNull(_baselines.Find(42, false, "1.1.0", ConfigPath));
    }

    [Fact]
    public void RemovingAModDropsItsBaselines()
    {
        Write(ConfigPath, "{ \"a\": 1 }");
        _baselines.Capture(_install, 42, false, "1.0.0", [ConfigPath]);

        _baselines.Remove(42, isAddon: false);

        Assert.Null(_baselines.Find(42, false, "1.0.0", ConfigPath));
    }

    // An addon and a mod can share an id, so their baselines must not share a folder.
    [Fact]
    public void AnAddonsBaselineIsSeparateFromAModsWithTheSameId()
    {
        Write(ConfigPath, "{ \"a\": 1 }");
        _baselines.Capture(_install, 42, true, "1.0.0", [ConfigPath]);

        Assert.Null(_baselines.Find(42, false, "1.0.0", ConfigPath));
        Assert.NotNull(_baselines.Find(42, true, "1.0.0", ConfigPath));
    }
    //
    // Stage 2: the merge itself. The pipeline is the same; what changes is what lands on disk.
    //

    [Fact]
    public void AnEditedSettingIsCarriedIntoTheNewDefaults()
    {
        Write(ConfigPath, "{\n  // how many\n  \"count\": 1,\n  \"name\": \"a\"\n}");
        _baselines.Capture(_install, 42, false, "1.0.0", [ConfigPath]);
        Write(ConfigPath, "{\n  // how many\n  \"count\": 7,\n  \"name\": \"a\"\n}");

        var report = Update(
            Record("1.0.0", ConfigPath),
            "1.1.0",
            (ConfigPath, "{\n  // how many, now with more words\n  \"count\": 2,\n  \"name\": \"a\",\n  \"extra\": true\n}"));

        var outcome = Only(report, ConfigPath);
        Assert.Equal(ConfigOutcomeKind.Merged, outcome.Kind);
        Assert.Equal(["count"], outcome.Carried);

        var merged = Read(ConfigPath);
        Assert.Contains("\"count\": 7", merged);
        Assert.Contains("\"extra\": true", merged);
        Assert.Contains("// how many, now with more words", merged);
    }

    // A setting the new version has dropped is reported rather than smuggled back in.
    [Fact]
    public void ASettingTheNewVersionDroppedIsReported()
    {
        Write(ConfigPath, "{ \"gone\": 1 }");
        _baselines.Capture(_install, 42, false, "1.0.0", [ConfigPath]);
        Write(ConfigPath, "{ \"gone\": 5 }");

        var report = Update(Record("1.0.0", ConfigPath), "1.1.0", (ConfigPath, "{ \"kept\": 1 }"));

        var outcome = Only(report, ConfigPath);
        Assert.Equal(ConfigOutcomeKind.Merged, outcome.Kind);
        Assert.Equal(["gone"], outcome.Dropped);
        Assert.Empty(outcome.Carried);
    }

    [Fact]
    public void KeepMinePutsTheUsersFileBack()
    {
        Write(ConfigPath, "{ \"mine\": true }");
        _options.SetPolicy("TestMod", ModConfigPolicy.KeepMine);

        var report = Update(Record("1.0.0", ConfigPath), "1.1.0", (ConfigPath, "{ \"mine\": false }"));

        Assert.Equal(ConfigOutcomeKind.KeptMine, Only(report, ConfigPath).Kind);
        Assert.Equal("{ \"mine\": true }", Read(ConfigPath));
    }

    // Keep mine needs no baseline - it is the answer for a mod the merge can't reason about.
    [Fact]
    public void KeepMineNeedsNoBaseline()
    {
        Write(ConfigPath, "{ \"mine\": true }");
        _options.SetPolicy("TestMod", ModConfigPolicy.KeepMine);

        var report = Update(existing: null, "1.1.0", (ConfigPath, "{ \"mine\": false }"));

        Assert.Equal(ConfigOutcomeKind.KeptMine, Only(report, ConfigPath).Kind);
    }

    [Fact]
    public void TakeNewSaysThatIsWhyItReplacedTheFile()
    {
        Write(ConfigPath, "{ \"a\": 1 }");
        _baselines.Capture(_install, 42, false, "1.0.0", [ConfigPath]);
        Write(ConfigPath, "{ \"a\": 99 }");
        _options.SetPolicy("TestMod", ModConfigPolicy.TakeNew);

        var report = Update(Record("1.0.0", ConfigPath), "1.1.0", (ConfigPath, "{ \"a\": 2 }"));

        var outcome = Only(report, ConfigPath);
        Assert.Equal(ConfigOutcomeKind.Replaced, outcome.Kind);
        Assert.Equal(ConfigReplaceReason.TakeNewPolicy, outcome.Reason);
        Assert.Equal("{ \"a\": 2 }", Read(ConfigPath));
    }

    // Data rather than settings: past the bound, the file is replaced and says so.
    [Fact]
    public void AFileTooLargeToBeSettingsIsNotMerged()
    {
        var big = "{" + string.Join(",", Enumerable.Range(0, JsonConfigMerge.MaxValues + 1).Select(i => $"\"k{i}\": {i}")) + "}";

        Write(ConfigPath, big);
        _baselines.Capture(_install, 42, false, "1.0.0", [ConfigPath]);
        Write(ConfigPath, big.Replace("\"k0\": 0", "\"k0\": 999"));

        var report = Update(Record("1.0.0", ConfigPath), "1.1.0", (ConfigPath, big));

        var outcome = Only(report, ConfigPath);
        Assert.Equal(ConfigOutcomeKind.Replaced, outcome.Kind);
        Assert.Equal(ConfigReplaceReason.TooLarge, outcome.Reason);
    }

    [Fact]
    public void AFileTheMergeCannotReadIsReplacedAndSaysSo()
    {
        // Unquoted keys - JSON5, which Utf8JsonReader cannot read.
        Write(ConfigPath, "{ count: 1 }");
        _baselines.Capture(_install, 42, false, "1.0.0", [ConfigPath]);
        Write(ConfigPath, "{ count: 7 }");

        var report = Update(Record("1.0.0", ConfigPath), "1.1.0", (ConfigPath, "{ count: 2 }"));

        var outcome = Only(report, ConfigPath);
        Assert.Equal(ConfigOutcomeKind.Replaced, outcome.Kind);
        Assert.Equal(ConfigReplaceReason.NotMergeable, outcome.Reason);
    }

    // The policy is asked of the mod's folder, so a mod with a client and a server half answers the
    // same either way.
    [Fact]
    public void ThePolicyIsFoundFromAnyOfTheModsFolders()
    {
        _options.SetPolicy("TestMod", ModConfigPolicy.TakeNew);

        Assert.Equal(ModConfigPolicy.TakeNew, _options.PolicyFor(["Something", "testmod"]));
        Assert.Equal(ModConfigPolicy.Merge, _options.PolicyFor(["Something"]));
    }
}
