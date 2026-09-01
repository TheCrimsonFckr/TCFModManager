using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class ModFootprintAnalyzerTests : IDisposable
{
    private readonly string _root;

    public ModFootprintAnalyzerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "TCFModManagerFootprintTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    private string NewFolder(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static string WriteFile(string folder, string name, int bytes)
    {
        var path = Path.Combine(folder, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    private static InstalledMod Entry(
        string folderPath,
        InstalledModTarget target = InstalledModTarget.Client,
        bool isPatcher = false) =>
        new()
        {
            Name = Path.GetFileName(folderPath),
            Target = target,
            IsPatcher = isPatcher,
            FolderPath = folderPath,
        };

    // A real managed assembly to read - Core's own, which has no patches and no Unity types, so it
    // proves the reader reaches the end of a genuine assembly and reports nothing rather than
    // failing or inventing counts.
    private static string CoreAssemblyPath => typeof(ModFootprint).Assembly.Location;

    [Fact]
    public void CountsFilesAndBytesAcrossSubfolders()
    {
        var folder = NewFolder("Example");
        WriteFile(folder, "a.txt", 100);
        WriteFile(folder, Path.Combine("nested", "b.txt"), 50);

        var footprint = ModFootprintAnalyzer.Analyze([Entry(folder)]);

        Assert.Equal(2, footprint.FileCount);
        Assert.Equal(150, footprint.TotalBytes);
    }

    [Fact]
    public void BundlesAreCountedSeparatelyAndStillCountAsFiles()
    {
        var folder = NewFolder("Bundled");
        WriteFile(folder, "assets.bundle", 400);
        WriteFile(folder, "readme.txt", 10);

        var footprint = ModFootprintAnalyzer.Analyze([Entry(folder)]);

        Assert.Equal(1, footprint.BundleCount);
        Assert.Equal(400, footprint.BundleBytes);
        Assert.Equal(2, footprint.FileCount);
        Assert.Equal(410, footprint.TotalBytes);
    }

    [Fact]
    public void ReadsARealManagedAssemblyAndReportsNoPatches()
    {
        var folder = NewFolder("Managed");
        File.Copy(CoreAssemblyPath, Path.Combine(folder, "TCFModManager.Core.dll"));

        var footprint = ModFootprintAnalyzer.Analyze([Entry(folder)]);

        Assert.Equal(1, footprint.AssemblyCount);
        Assert.Equal(0, footprint.UnreadableAssemblyCount);
        Assert.Equal(0, footprint.PatchClassCount);
        Assert.Empty(footprint.PerFrameMethods);
        Assert.Equal(ModFootprintLevel.Light, footprint.Level);
    }

    [Fact]
    public void ADllThatIsNotAManagedAssemblyIsNotCountedAsUnreadable()
    {
        // Mods ship native DLLs. Counting one as an assembly that failed to read would push the
        // whole mod to Unknown and hide everything else that was read correctly.
        var folder = NewFolder("Native");
        File.WriteAllBytes(Path.Combine(folder, "native.dll"), [0x4D, 0x5A, 0x00, 0x01, 0x02, 0x03]);

        var footprint = ModFootprintAnalyzer.Analyze([Entry(folder)]);

        Assert.Equal(0, footprint.AssemblyCount);
        Assert.Equal(0, footprint.UnreadableAssemblyCount);
        Assert.NotEqual(ModFootprintLevel.Unknown, footprint.Level);
    }

    [Fact]
    public void AnEmptyFileNamedDllIsIgnored()
    {
        var folder = NewFolder("Empty");
        WriteFile(folder, "broken.dll", 0);

        var footprint = ModFootprintAnalyzer.Analyze([Entry(folder)]);

        Assert.Equal(0, footprint.AssemblyCount);
        Assert.Equal(1, footprint.FileCount);
    }

    [Fact]
    public void HandlesALooseDllRecordedAsTheModPath()
    {
        // The scanner records a loose client DLL with FolderPath pointing at the DLL itself.
        var footprint = ModFootprintAnalyzer.Analyze([Entry(CoreAssemblyPath)]);

        Assert.Equal(1, footprint.FileCount);
        Assert.Equal(1, footprint.AssemblyCount);
    }

    [Fact]
    public void MergesEveryEntryOneCardCovers()
    {
        var plugin = NewFolder("Mod");
        WriteFile(plugin, "mod.txt", 10);
        var patcher = NewFolder("Mod.Patcher");
        WriteFile(patcher, "patch.txt", 20);
        var server = NewFolder("mod-server");
        WriteFile(server, "package.json", 30);

        var footprint = ModFootprintAnalyzer.Analyze(
        [
            Entry(plugin),
            Entry(patcher, isPatcher: true),
            Entry(server, InstalledModTarget.Server),
        ]);

        Assert.Equal(3, footprint.FileCount);
        Assert.Equal(60, footprint.TotalBytes);
        Assert.True(footprint.HasPatcher);
        Assert.True(footprint.HasServerHalf);
    }

    [Fact]
    public void PerFrameTypeCountIsZeroForAnAssemblyWithNoComponents()
    {
        var folder = NewFolder("NoComponents");
        File.Copy(CoreAssemblyPath, Path.Combine(folder, "TCFModManager.Core.dll"));

        var footprint = ModFootprintAnalyzer.Analyze([Entry(folder)]);

        Assert.Equal(0, footprint.PerFrameTypeCount);
        Assert.False(footprint.HasPerFrameCode);
    }

    [Fact]
    public void KeyComesFromTheFirstEntryAndIsLowercased()
    {
        var folder = NewFolder("MixedCaseName");

        var footprint = ModFootprintAnalyzer.Analyze([Entry(folder)]);

        Assert.Equal(folder.ToLowerInvariant(), footprint.FolderKey);
        Assert.Equal(ModFootprintAnalyzer.KeyFor(folder), footprint.FolderKey);
    }

    [Fact]
    public void AMissingFolderProducesAnEmptyFootprintRatherThanThrowing()
    {
        var footprint = ModFootprintAnalyzer.Analyze([Entry(Path.Combine(_root, "gone"))]);

        Assert.Equal(0, footprint.FileCount);
        Assert.Equal(0, footprint.TotalBytes);
        Assert.Equal(ModFootprintLevel.Light, footprint.Level);
    }

    [Fact]
    public void AnalyzeRequiresAtLeastOneEntry()
    {
        Assert.Throws<ArgumentException>(() => ModFootprintAnalyzer.Analyze([]));
    }

    [Fact]
    public void StampIsStableForAnUnchangedFolder()
    {
        var folder = NewFolder("Stable");
        WriteFile(folder, "a.txt", 10);

        var first = ModFootprintAnalyzer.StampFor([Entry(folder)]);
        var second = ModFootprintAnalyzer.StampFor([Entry(folder)]);

        Assert.Equal(first, second);
    }

    [Fact]
    public void StampChangesWhenAFileIsAdded()
    {
        var folder = NewFolder("Grown");
        WriteFile(folder, "a.txt", 10);
        var before = ModFootprintAnalyzer.StampFor([Entry(folder)]);

        WriteFile(folder, "b.txt", 10);

        Assert.NotEqual(before, ModFootprintAnalyzer.StampFor([Entry(folder)]));
    }

    [Fact]
    public void StampChangesWhenAFileGrows()
    {
        var folder = NewFolder("Edited");
        WriteFile(folder, "a.txt", 10);
        var before = ModFootprintAnalyzer.StampFor([Entry(folder)]);

        WriteFile(folder, "a.txt", 4096);

        Assert.NotEqual(before, ModFootprintAnalyzer.StampFor([Entry(folder)]));
    }

    [Fact]
    public void AnalyzeStampsWhatItRead()
    {
        var folder = NewFolder("Stamped");
        WriteFile(folder, "a.txt", 10);

        var footprint = ModFootprintAnalyzer.Analyze([Entry(folder)]);

        Assert.Equal(ModFootprintAnalyzer.StampFor([Entry(folder)]), footprint.Stamp);
    }

    [Theory]
    [InlineData("Update")]
    [InlineData("LateUpdate")]
    [InlineData("FixedUpdate")]
    [InlineData("OnGUI")]
    public void PerFrameMethodNamesAreRecognised(string name)
    {
        Assert.True(ModFootprintAnalyzer.IsPerFrameMethodName(name));
    }

    [Theory]
    [InlineData("Awake")]
    [InlineData("Start")]
    [InlineData("OnDestroy")]
    [InlineData("update")]
    public void MethodsThatDoNotRunEveryFrameAreNot(string name)
    {
        Assert.False(ModFootprintAnalyzer.IsPerFrameMethodName(name));
    }

    [Theory]
    [InlineData("MonoBehaviour")]
    [InlineData("BaseUnityPlugin")]
    [InlineData("UIElement")]
    public void UnityComponentBasesAreRecognised(string name)
    {
        Assert.True(ModFootprintAnalyzer.IsUnityComponentBase(name));
    }

    [Theory]
    [InlineData("Object")]
    [InlineData("ModulePatch")]
    [InlineData(null)]
    public void OtherBasesAreNotUnityComponents(string? name)
    {
        Assert.False(ModFootprintAnalyzer.IsUnityComponentBase(name));
    }
}
