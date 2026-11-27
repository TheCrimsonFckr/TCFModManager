using System.Text.Json;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class HeadlessAdviceReaderTests
{
    private sealed class Sample
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public HeadlessVerdict Expected { get; set; }
        public string? Note { get; set; }
        public string Html { get; set; } = "";
    }

    private static readonly JsonSerializerOptions SampleOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private static readonly Lazy<List<Sample>> Samples = new(() =>
        JsonSerializer.Deserialize<List<Sample>>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "HeadlessAdvice", "descriptions.json")),
            SampleOptions)!);

    [Fact]
    public void EverySampledDescriptionReadsAsRecorded()
    {
        var wrong = Samples.Value
            .Select(s => (s, got: HeadlessAdviceReader.Read(s.Html).Verdict))
            .Where(x => x.got != x.s.Expected)
            .Select(x => $"[{x.s.Id}] {x.s.Name}: expected {x.s.Expected}, read {x.got}")
            .ToList();

        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    [Fact]
    public void TheThirteenKeepOffMods_AreExactlyTheAvoids()
    {
        int[] keepOff = [1305, 1431, 1853, 2101, 2144, 2173, 2299, 2518, 2552, 2935, 2955, 3011, 3139];

        var avoids = Samples.Value
            .Where(s => HeadlessAdviceReader.Read(s.Html).Verdict == HeadlessVerdict.Avoid)
            .Select(s => s.Id)
            .Order()
            .ToArray();

        Assert.Equal(keepOff, avoids);
    }

    [Theory]
    [InlineData("<p>Just install it on all players\u2019 computers, except the headless client</p>")]
    [InlineData("<p>Install it on every PC excluding the Fika headless.</p>")]
    [InlineData("<p>All clients need it, apart from the dedicated client.</p>")]
    public void AHeadlessNamedAsAnException_IsKeptOff(string html) =>
        Assert.Equal(HeadlessVerdict.Avoid, HeadlessAdviceReader.Read(html).Verdict);

    [Fact]
    public void VramCleaner_QuotesItsInstallStep()
    {
        var advice = HeadlessAdviceReader.Read(Html(2173));

        Assert.Equal(HeadlessVerdict.Avoid, advice.Verdict);
        Assert.Contains(advice.Quotes, q => q.Contains("install onto a fika headless client", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ADotInsideAFileName_DoesNotEndTheSentence()
    {
        var advice = HeadlessAdviceReader.Read(Html(2955));

        Assert.Equal(HeadlessVerdict.Avoid, advice.Verdict);
        Assert.Contains(advice.Quotes, q => q.Contains("QuickSell.dll", StringComparison.Ordinal));
    }

    [Fact]
    public void AHeading_IsReadWithTheSentencesAfterIt()
    {
        var advice = HeadlessAdviceReader.Read(
            "<h3>Fika Headless</h3><p>Splendid news: Do not install anything there. Seriously.</p>");

        Assert.Equal(HeadlessVerdict.Avoid, advice.Verdict);
    }

    [Fact]
    public void HarmlessOnAHeadless_IsAMentionNotAnAvoid()
    {
        Assert.Equal(HeadlessVerdict.Mentions, HeadlessAdviceReader.Read(Html(2860)).Verdict);
        Assert.Equal(HeadlessVerdict.Mentions, HeadlessAdviceReader.Read(Html(3097)).Verdict);
    }

    [Fact]
    public void NotRequiredOnAHeadless_IsAnAvoid()
    {
        var advice = HeadlessAdviceReader.Read(
            "<p>The client mod is not required to be installed on headless client if you use that.</p>");

        Assert.Equal(HeadlessVerdict.Avoid, advice.Verdict);
    }

    // OPEN-44 R9: the shape Stance Overhaul Fika Sync uses. Needs, and only this.
    [Fact]
    public void HeadlessWantsOnlyTheSync_IsNeedsAndOnlyThis()
    {
        var advice = HeadlessAdviceReader.Read("<p>Headless client : ONLY FIKA SYNC</p><p>Player client : everything</p>");

        Assert.Equal(HeadlessVerdict.Needs, advice.Verdict);
        Assert.True(advice.OnlyThis);

        var prose = HeadlessAdviceReader.Read("<p>On the headless you only need the sync addon, nothing else from this mod.</p>");

        Assert.Equal(HeadlessVerdict.Needs, prose.Verdict);
        Assert.True(prose.OnlyThis);
    }

    [Fact]
    public void AnOrdinaryNeeds_IsNotOnlyThis()
    {
        var advice = HeadlessAdviceReader.Read("<p>Install this on every client including the headless.</p>");

        Assert.Equal(HeadlessVerdict.Needs, advice.Verdict);
        Assert.False(advice.OnlyThis);
    }

    [Fact]
    public void SayingBothThings_IsOnlyAMention()
    {
        var advice = HeadlessAdviceReader.Read(
            "<p>Do not install this mod on the headless client.</p><p>Install this mod on every client including headless.</p>");

        Assert.Equal(HeadlessVerdict.Mentions, advice.Verdict);
        Assert.Equal(2, advice.Quotes.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<p>Adds a new gun.</p>")]
    public void NothingAboutAHeadless_IsNone(string? html) =>
        Assert.Same(HeadlessAdvice.Nothing, HeadlessAdviceReader.Read(html));

    [Fact]
    public void EntitiesAndBreaks_AreReadAsText()
    {
        var sentences = HeadlessAdviceReader.Sentences("<p>Don&rsquo;t install<br>on a headless.</p><ul><li>One.</li><li>Two.</li></ul>");

        Assert.Equal(["Don\u2019t install", "on a headless.", "One.", "Two."], sentences);
    }

    private static string Html(int id) => Samples.Value.Single(s => s.Id == id).Html;
}
