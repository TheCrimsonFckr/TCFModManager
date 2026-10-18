using System.Net;
using System.Text;
using System.Text.Json;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using TCFModManager.Core.SpModApi;
using Xunit;

namespace TCFModManager.Core.Tests;

public class HeadlessAdviceServiceTests
{
    private const string VramCleaner = "<ol><li>Move Bepinex folder into your SPT folder</li><li><strong>Don\u2019t</strong> install onto a fika headless client</li></ol>";

    private static readonly DateTimeOffset Edited = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private sealed class Routes : HttpMessageHandler
    {
        public Dictionary<string, Func<HttpResponseMessage>> ByPath { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Seen { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Seen.Add(path);
            return Task.FromResult(ByPath.TryGetValue(path, out var respond)
                ? respond()
                : new HttpResponseMessage(HttpStatusCode.NotFound) { Content = Body(new { success = false, code = "NOT_FOUND", message = "nope" }) });
        }

        public void Mod(int id, string description, DateTimeOffset updatedAt) =>
            ByPath[$"/api/v0/mod/{id}"] = () => Ok(new { id, description, updated_at = updatedAt });

        public void Addon(int id, string description, DateTimeOffset updatedAt) =>
            ByPath[$"/api/v0/addon/{id}"] = () => Ok(new { id, description, updated_at = updatedAt });

        private static HttpResponseMessage Ok(object data) =>
            new(HttpStatusCode.OK) { Content = Body(new { success = true, data }) };

        private static StringContent Body(object value) =>
            new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");
    }

    private static (HeadlessAdviceService Service, Routes Routes, string Path) Make(Func<DateTimeOffset>? now = null)
    {
        var routes = new Routes();
        var path = Path.Combine(Path.GetTempPath(), $"headless-advice-{Guid.NewGuid():N}.json");
        var service = new HeadlessAdviceService(
            new SpModApiClient(new HttpClient(routes)),
            new HeadlessAdviceStore(path),
            pauseBetweenFetches: TimeSpan.Zero,
            now: now);
        return (service, routes, path);
    }

    [Fact]
    public async Task FetchesReadsAndCaches_ThenServesFromTheCacheWhileUpdatedAtHolds()
    {
        var (service, routes, path) = Make();
        try
        {
            routes.Mod(2173, VramCleaner, Edited);
            var subject = HeadlessSubject.ForMod(2173, Edited);

            var first = await service.ResolveAsync([subject]);
            Assert.Equal(HeadlessVerdict.Avoid, first[subject].Verdict);
            Assert.Single(routes.Seen);

            var reloaded = new HeadlessAdviceService(
                new SpModApiClient(new HttpClient(routes)), new HeadlessAdviceStore(path), TimeSpan.Zero);
            var second = await reloaded.ResolveAsync([subject]);

            Assert.Equal(HeadlessVerdict.Avoid, second[subject].Verdict);
            Assert.Single(routes.Seen);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ANewerUpdatedAtInTheCatalog_FetchesAgain()
    {
        var (service, routes, path) = Make();
        try
        {
            routes.Mod(2173, VramCleaner, Edited);
            await service.ResolveAsync([HeadlessSubject.ForMod(2173, Edited)]);

            var later = Edited.AddDays(3);
            routes.Mod(2173, "<p>Install this mod on every client including headless.</p>", later);
            var subject = HeadlessSubject.ForMod(2173, later);
            var resolved = await service.ResolveAsync([subject]);

            Assert.Equal(HeadlessVerdict.Needs, resolved[subject].Verdict);
            Assert.Equal(2, routes.Seen.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task AnOlderCatalogThanTheCache_DoesNotRefetch()
    {
        var (service, routes, path) = Make();
        try
        {
            routes.Mod(2173, VramCleaner, Edited);
            await service.ResolveAsync([HeadlessSubject.ForMod(2173, Edited)]);
            await service.ResolveAsync([HeadlessSubject.ForMod(2173, Edited.AddDays(-2))]);

            Assert.Single(routes.Seen);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task WithoutAnUpdatedAt_TheCacheHoldsForSevenDays()
    {
        var clock = Edited;
        var (service, routes, path) = Make(() => clock);
        try
        {
            routes.Addon(167, "<p>Headless client requires bundles folder and all dlls.</p>", Edited);
            var subject = HeadlessSubject.ForAddon(167);

            await service.ResolveAsync([subject]);
            clock = Edited.AddDays(6);
            await service.ResolveAsync([subject]);
            Assert.Single(routes.Seen);

            clock = Edited.AddDays(8);
            await service.ResolveAsync([subject]);
            Assert.Equal(2, routes.Seen.Count);
            Assert.All(routes.Seen, p => Assert.Equal("/api/v0/addon/167", p));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ANotListedMod_IsLeftOutAndNotCached()
    {
        var (service, routes, path) = Make();
        try
        {
            var subject = HeadlessSubject.ForMod(99999, Edited);

            Assert.Empty(await service.ResolveAsync([subject]));
            Assert.Empty(await service.ResolveAsync([subject]));
            Assert.Equal(2, routes.Seen.Count);
            Assert.Null(service.Known(subject));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task RateLimited_BacksOffAndRetries()
    {
        var (service, routes, path) = Make();
        try
        {
            var calls = 0;
            routes.ByPath["/api/v0/mod/2173"] = () =>
            {
                if (calls++ == 0)
                {
                    var limited = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                    {
                        Content = new StringContent("{\"success\":false,\"code\":\"RATE_LIMITED\",\"message\":\"slow down\"}", Encoding.UTF8, "application/json"),
                    };
                    limited.Headers.Add("Retry-After", "0");
                    return limited;
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new { success = true, data = new { id = 2173, description = VramCleaner, updated_at = Edited } }), Encoding.UTF8, "application/json"),
                };
            };

            var subject = HeadlessSubject.ForMod(2173, Edited);
            var resolved = await service.ResolveAsync([subject]);

            Assert.Equal(HeadlessVerdict.Avoid, resolved[subject].Verdict);
            Assert.Equal(2, calls);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ReportsCachedThenFetched_AndAsksOncePerSubject()
    {
        var (service, routes, path) = Make();
        try
        {
            routes.Mod(2173, VramCleaner, Edited);
            routes.Mod(1342, "<p>You MUST install the same version of UI Fixes on all clients, including the dedicated client if you\u2019re using that.</p>", Edited);
            await service.ResolveAsync([HeadlessSubject.ForMod(2173, Edited)]);

            var reported = new List<HeadlessAdviceResolved>();
            await service.ResolveAsync(
                [HeadlessSubject.ForMod(2173, Edited), HeadlessSubject.ForMod(1342, Edited), HeadlessSubject.ForMod(1342, Edited)],
                new SyncProgress<HeadlessAdviceResolved>(reported.Add));

            Assert.Collection(reported,
                r => { Assert.Equal(2173, r.Subject.Id); Assert.True(r.FromCache); },
                r => { Assert.Equal(1342, r.Subject.Id); Assert.False(r.FromCache); Assert.Equal(HeadlessVerdict.Needs, r.Advice.Verdict); });
            Assert.Equal(2, routes.Seen.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task AModAndAnAddonWithTheSameId_AreDifferentSubjects()
    {
        var (service, routes, path) = Make();
        try
        {
            routes.Mod(167, VramCleaner, Edited);
            routes.Addon(167, "<p>Adds stickers.</p>", Edited);

            var resolved = await service.ResolveAsync([HeadlessSubject.ForMod(167, Edited), HeadlessSubject.ForAddon(167, Edited)]);

            Assert.Equal(HeadlessVerdict.Avoid, resolved[HeadlessSubject.ForMod(167, Edited)].Verdict);
            Assert.Equal(HeadlessVerdict.None, resolved[HeadlessSubject.ForAddon(167, Edited)].Verdict);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
