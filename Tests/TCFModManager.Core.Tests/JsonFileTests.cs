using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class JsonFileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tcfmm-jsonfile-" + Guid.NewGuid().ToString("N"));

    public JsonFileTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    private sealed class Sample
    {
        public int Number { get; set; }
        public List<string> Names { get; set; } = [];
    }

    private string P(string name) => Path.Combine(_root, name);

    private IEnumerable<string> Damaged(string name) =>
        Directory.EnumerateFiles(_root, name + ".damaged-*");

    [Fact]
    public void Read_ReturnsNullForAMissingFile()
    {
        Assert.Null(new JsonFile<Sample>(P("missing.json")).Read());
        Assert.Empty(Directory.EnumerateFileSystemEntries(_root));
    }

    [Fact]
    public void Read_ReturnsNullForAJsonNullLiteral()
    {
        File.WriteAllText(P("null.json"), "null");

        Assert.Null(new JsonFile<Sample>(P("null.json")).Read());
        Assert.Empty(Damaged("null.json"));
    }

    [Fact]
    public void Read_KeepsADamagedFileAsideAndReturnsNull()
    {
        File.WriteAllText(P("bad.json"), "{ not json");

        Assert.Null(new JsonFile<Sample>(P("bad.json")).Read());
        Assert.Equal("{ not json", File.ReadAllText(Assert.Single(Damaged("bad.json"))));
        Assert.Equal("{ not json", File.ReadAllText(P("bad.json")));
    }

    [Fact]
    public void Read_LeavesNoDamagedCopyWhenNotAskedTo()
    {
        File.WriteAllText(P("cache.json"), "{ not json");

        Assert.Null(new JsonFile<Sample>(P("cache.json"), preserveDamaged: false).Read());
        Assert.Empty(Damaged("cache.json"));
    }

    [Fact]
    public void Read_LetsAnIOErrorThrough()
    {
        File.WriteAllText(P("locked.json"), "{}");

        using var held = new FileStream(P("locked.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        Assert.ThrowsAny<IOException>(() => new JsonFile<Sample>(P("locked.json")).Read());
    }

    [Fact]
    public void Write_ThenRead_RoundTrips_IntoAFolderThatDidNotExist()
    {
        var file = new JsonFile<Sample>(P(Path.Combine("sub", "data.json")), JsonFileOptions.Indented);

        file.Write(new Sample { Number = 7, Names = ["a", "b"] });
        var back = file.Read();

        Assert.Equal(7, back?.Number);
        Assert.Equal(["a", "b"], back?.Names);
        Assert.Contains("\n", File.ReadAllText(file.FilePath));
    }

    [Fact]
    public void Write_KeepsABackupOfTheOldFileWhenAskedTo()
    {
        var file = new JsonFile<Sample>(P("kept.json"), keepBackups: true);

        file.Write(new Sample { Number = 1 });
        file.Write(new Sample { Number = 2 });

        var backup = Assert.Single(Directory.EnumerateFiles(SafeFile.BackupFolderFor(file.FilePath)));
        Assert.Contains("\"Number\":1", File.ReadAllText(backup));
        Assert.Equal(2, file.Read()?.Number);
    }

    [Fact]
    public void Write_KeepsNoBackupByDefault()
    {
        var file = new JsonFile<Sample>(P("plain.json"));

        file.Write(new Sample { Number = 1 });
        file.Write(new Sample { Number = 2 });

        Assert.False(Directory.Exists(SafeFile.BackupFolderFor(file.FilePath)));
    }
}
