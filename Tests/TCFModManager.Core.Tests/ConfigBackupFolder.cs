using Xunit;

namespace TCFModManager.Core.Tests;

//
// ModConfigStore.BackupDirectory is one folder beside the test assembly, shared by every test that
// saves a config. xUnit runs test classes in parallel, and ModConfigStoreTests clears the whole
// folder after each test - so a class running alongside it had its backups deleted mid-test.
// Classes in this collection run one at a time.
//
[CollectionDefinition(Name)]
public sealed class ConfigBackupFolder
{
    public const string Name = "config-backup-folder";
}
