using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class HeadlessAdviceScopesTests
{
    private const ModListEntryScope Client = ModListEntryScope.Client;
    private const ModListEntryScope Server = ModListEntryScope.Server;
    private const ModListEntryScope Headless = ModListEntryScope.Headless;
    private const ModListEntryScope Everyone = ModListEntryScope.Everyone;

    [Theory]
    [InlineData(Client | Headless, Client)]
    [InlineData(Everyone, Client | Server)]
    public void AvoidDropsTheHeadless(ModListEntryScope scope, ModListEntryScope suggested) =>
        Assert.Equal(suggested, HeadlessAdviceScopes.Suggest(HeadlessVerdict.Avoid, scope));

    [Theory]
    [InlineData(Headless)]
    [InlineData(Client)]
    [InlineData(Server)]
    [InlineData(Client | Server)]
    public void AvoidSuggestsNothing_WhenTheHeadlessIsAlreadyOffOrIsTheOperatorsChoice(ModListEntryScope scope) =>
        Assert.Null(HeadlessAdviceScopes.Suggest(HeadlessVerdict.Avoid, scope));

    [Theory]
    [InlineData(Client, Client | Headless)]
    [InlineData(Client | Server, Everyone)]
    public void NeedsPutsTheHeadlessBack_AndIsAWarning(ModListEntryScope scope, ModListEntryScope suggested)
    {
        Assert.Equal(suggested, HeadlessAdviceScopes.Suggest(HeadlessVerdict.Needs, scope));
        Assert.True(HeadlessAdviceScopes.IsWarning(HeadlessVerdict.Needs, scope));
    }

    [Theory]
    [InlineData(Client | Headless)]
    [InlineData(Everyone)]
    [InlineData(Headless)]
    [InlineData(Server)]
    public void NeedsIsOnlyABadge_WhenTheHeadlessAlreadyGetsIt(ModListEntryScope scope)
    {
        Assert.Null(HeadlessAdviceScopes.Suggest(HeadlessVerdict.Needs, scope));
        Assert.False(HeadlessAdviceScopes.IsWarning(HeadlessVerdict.Needs, scope));
    }

    [Theory]
    [InlineData(HeadlessVerdict.None)]
    [InlineData(HeadlessVerdict.Mentions)]
    public void NoneAndMentions_NeverSuggest(HeadlessVerdict verdict)
    {
        foreach (var scope in new[] { Everyone, Client | Headless, Client, Headless, Server, Client | Server })
            Assert.Null(HeadlessAdviceScopes.Suggest(verdict, scope));
    }

    [Fact]
    public void AnAddon_UsesItsOwnAdviceFirst()
    {
        var own = new HeadlessAdvice(HeadlessVerdict.Needs, ["own"]);
        var parent = new HeadlessAdvice(HeadlessVerdict.Avoid, ["parent"]);

        Assert.Equal(new HeadlessAdviceForEntry(own, FromParent: false), HeadlessAdviceScopes.ForAddon(own, parent));
    }

    [Fact]
    public void AnAddonThatSaysNothing_TakesItsParentsAdvice()
    {
        var parent = new HeadlessAdvice(HeadlessVerdict.Avoid, ["parent"]);

        Assert.Equal(new HeadlessAdviceForEntry(parent, FromParent: true), HeadlessAdviceScopes.ForAddon(HeadlessAdvice.Nothing, parent));
    }

    [Fact]
    public void AnAddonNotReadYet_HasNoAdvice() =>
        Assert.Null(HeadlessAdviceScopes.ForAddon(null, new HeadlessAdvice(HeadlessVerdict.Avoid, ["parent"])));

    [Fact]
    public void NeitherSayingAnything_IsNone()
    {
        var result = HeadlessAdviceScopes.ForAddon(HeadlessAdvice.Nothing, HeadlessAdvice.Nothing);

        Assert.NotNull(result);
        Assert.Equal(HeadlessVerdict.None, result.Advice.Verdict);
        Assert.False(result.FromParent);
    }
}
