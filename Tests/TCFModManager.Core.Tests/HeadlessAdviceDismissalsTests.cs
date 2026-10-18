using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class HeadlessAdviceDismissalsTests
{
    private static readonly Guid ListA = Guid.Parse("7f3c1a52-0d55-4f0e-9a0c-2b8f0b6c1e11");
    private static readonly Guid ListB = Guid.Parse("1d2e3f40-5a6b-4c7d-8e9f-a0b1c2d3e4f5");

    [Fact]
    public void AKeepHoldsForThatListModAndVerdictOnly_AndSurvivesAReload()
    {
        var path = Path.Combine(Path.GetTempPath(), $"headless-kept-{Guid.NewGuid():N}.json");
        try
        {
            var key = HeadlessAdviceDismissals.Key(ListA, HeadlessSubject.ForMod(2173), HeadlessVerdict.Avoid);
            new HeadlessAdviceDismissals(path).Keep(key);

            var kept = new HeadlessAdviceDismissals(path).Load();

            Assert.Contains(key, kept);
            Assert.DoesNotContain(HeadlessAdviceDismissals.Key(ListB, HeadlessSubject.ForMod(2173), HeadlessVerdict.Avoid), kept);
            Assert.DoesNotContain(HeadlessAdviceDismissals.Key(ListA, HeadlessSubject.ForMod(2144), HeadlessVerdict.Avoid), kept);
            Assert.DoesNotContain(HeadlessAdviceDismissals.Key(ListA, HeadlessSubject.ForAddon(2173), HeadlessVerdict.Avoid), kept);
            Assert.DoesNotContain(HeadlessAdviceDismissals.Key(ListA, HeadlessSubject.ForMod(2173), HeadlessVerdict.Needs), kept);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TheKeyIgnoresTheCatalogDate() =>
        Assert.Equal(
            HeadlessAdviceDismissals.Key(ListA, HeadlessSubject.ForMod(2173, DateTimeOffset.UnixEpoch), HeadlessVerdict.Avoid),
            HeadlessAdviceDismissals.Key(ListA, HeadlessSubject.ForMod(2173), HeadlessVerdict.Avoid));

    [Fact]
    public void KeepingTwiceWritesOnce_AndAMissingFileIsEmpty()
    {
        var path = Path.Combine(Path.GetTempPath(), $"headless-kept-{Guid.NewGuid():N}.json");
        try
        {
            var store = new HeadlessAdviceDismissals(path);
            Assert.Empty(store.Load());

            var key = HeadlessAdviceDismissals.Key(ListA, HeadlessSubject.ForMod(1), HeadlessVerdict.Avoid);
            store.Keep(key);
            store.Keep(key);

            Assert.Single(store.Load());
        }
        finally
        {
            File.Delete(path);
        }
    }
}
