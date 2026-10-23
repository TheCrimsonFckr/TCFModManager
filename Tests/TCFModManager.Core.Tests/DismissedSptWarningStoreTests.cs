using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class DismissedSptWarningStoreTests
{
    [Fact]
    public void ADismissalHoldsForThatVersionAndSptOnly_AndCanBeUndone()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dismissed-{Guid.NewGuid():N}.json");
        try
        {
            var store = new DismissedSptWarningStore(path);
            var key = DismissedSptWarningStore.Key("2543", "1.2.1", "4.1.6");

            store.Dismiss([key]);
            Assert.Contains(key, store.Load());
            Assert.DoesNotContain(DismissedSptWarningStore.Key("2543", "1.2.2", "4.1.6"), store.Load());
            Assert.DoesNotContain(DismissedSptWarningStore.Key("2543", "1.2.1", "4.1.7"), store.Load());

            store.Restore(key);
            Assert.Empty(store.Load());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_IsEmptyForACorruptFile_AndKeepsItAside()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dismissed-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{ not json");
            Assert.Empty(new DismissedSptWarningStore(path).Load());

            var damaged = Assert.Single(Directory.EnumerateFiles(Path.GetTempPath(), Path.GetFileName(path) + ".damaged-*"));
            Assert.Equal("{ not json", File.ReadAllText(damaged));
        }
        finally
        {
            File.Delete(path);
            foreach (var copy in Directory.EnumerateFiles(Path.GetTempPath(), Path.GetFileName(path) + ".damaged-*")) File.Delete(copy);
        }
    }
}
