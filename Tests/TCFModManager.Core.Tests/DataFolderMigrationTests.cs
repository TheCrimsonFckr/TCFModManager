using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

// OPEN-74: an older build's folder and file names, as on Chris's G: install, renamed once.
public class DataFolderMigrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tcfmm-names-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private string Write(string relative, string content = "x")
    {
        var full = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    private bool Exists(string relative)
    {
        var full = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        var parent = Path.GetDirectoryName(full)!;
        return Directory.Exists(parent)
            && Directory.EnumerateFileSystemEntries(parent).Select(Path.GetFileName).Contains(Path.GetFileName(full), StringComparer.Ordinal);
    }

    [Fact]
    public void Old_names_take_the_new_ones_with_what_is_in_them()
    {
        Write("Staging/archive.zip");
        Write("data/config_updates.json", "updates");
        Write("data/installed-mods.json", "manifest");
        Write("data/backups/config_updates.json/20261126-080050-295.json");
        Write("data/config-backups/20261009-090949/a.cfg");
        Write("data/LegacyConfigs/20261108-103609_MoreBotsAPI/config.json");
        Write("data/ServerMap/servermap-key.txt");
        Write("data/mod_groups.before-resort-2026-11-21.json");
        Write("data/settings.json");

        DataFolderMigration.Run(_root, Path.Combine(_root, "data"));

        Assert.True(Exists("staging/archive.zip"));
        Assert.Equal("updates", File.ReadAllText(Path.Combine(_root, "data", "configUpdates.json")));
        Assert.Equal("manifest", File.ReadAllText(Path.Combine(_root, "data", "installedMods.json")));
        Assert.True(Exists("data/backups/configUpdates.json/20261126-080050-295.json"));
        Assert.True(Exists("data/configBackups/20261009-090949/a.cfg"));
        Assert.True(Exists("data/legacyConfigs/20261108-103609_MoreBotsAPI/config.json"));
        Assert.False(Exists("data/config_updates.json"));
        Assert.False(Exists("data/config-backups"));

        Assert.True(Exists("data/ServerMap/servermap-key.txt"));
        Assert.True(Exists("data/mod_groups.before-resort-2026-11-21.json"));
        Assert.True(Exists("data/settings.json"));
    }

    [Fact]
    public void Both_names_there_leaves_the_old_one_alone()
    {
        Write("data/mod_lists.json", "old");
        Write("data/modLists.json", "new");

        DataFolderMigration.Run(_root, Path.Combine(_root, "data"));

        Assert.Equal("old", File.ReadAllText(Path.Combine(_root, "data", "mod_lists.json")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(_root, "data", "modLists.json")));
        Assert.Equal(DataFolderMigration.Outcome.BothKept, DataFolderMigration.Rename(Path.Combine(_root, "data"), "mod_lists.json", "modLists.json"));
    }

    [Fact]
    public void A_second_run_and_a_fresh_folder_do_nothing()
    {
        Write("data/mod_notes.json");
        DataFolderMigration.Run(_root, Path.Combine(_root, "data"));

        Assert.Equal(DataFolderMigration.Outcome.Nothing, DataFolderMigration.Rename(Path.Combine(_root, "data"), "mod_notes.json", "modNotes.json"));
        Assert.Equal(DataFolderMigration.Outcome.Nothing, DataFolderMigration.Rename(Path.Combine(_root, "nowhere"), "Data", "data"));
        Assert.True(Exists("data/modNotes.json"));
    }
}
