using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class AcceptedWarningStoreTests
{
    [Fact]
    public void Accept_IsKeptPerVersionPair()
    {
        var path = Path.Combine(Path.GetTempPath(), $"accepted-{Guid.NewGuid():N}.json");
        try
        {
            var store = new AcceptedWarningStore(path);
            var key = AcceptedWarningStore.Key(2882, "3.0.0", 2310, "3.0.6");

            Assert.False(store.IsAccepted(key));
            store.Accept(key);

            Assert.True(new AcceptedWarningStore(path).IsAccepted(key));
            Assert.False(store.IsAccepted(AcceptedWarningStore.Key(2882, "3.0.0", 2310, "3.0.7")));
            Assert.False(store.IsAccepted(AcceptedWarningStore.Key(2882, "3.0.1", 2310, "3.0.6")));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_IsEmptyForACorruptFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"accepted-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{ not json");
            Assert.Empty(new AcceptedWarningStore(path).Load());
        }
        finally
        {
            File.Delete(path);
        }
    }
}
