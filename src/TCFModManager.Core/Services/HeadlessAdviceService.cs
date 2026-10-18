using System.Net;
using TCFModManager.Core.Models;
using TCFModManager.Core.SpModApi;

namespace TCFModManager.Core.Services;

public sealed record HeadlessAdviceResolved(HeadlessSubject Subject, HeadlessAdvice Advice, bool FromCache);

public sealed class HeadlessAdviceService
{
    public static readonly TimeSpan UnkeyedMaxAge = TimeSpan.FromDays(7);

    private const int RateLimitRetries = 3;

    private readonly SpModApiClient _spModApi;
    private readonly HeadlessAdviceStore _store;
    private readonly TimeSpan _pause;
    private readonly Func<DateTimeOffset> _now;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Dictionary<string, HeadlessAdviceStore.Cached>? _cache;

    public HeadlessAdviceService(
        SpModApiClient spModApi,
        HeadlessAdviceStore? store = null,
        TimeSpan? pauseBetweenFetches = null,
        Func<DateTimeOffset>? now = null)
    {
        _spModApi = spModApi;
        _store = store ?? new HeadlessAdviceStore();
        _pause = pauseBetweenFetches ?? TimeSpan.FromMilliseconds(150);
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public HeadlessAdvice? Known(HeadlessSubject subject)
    {
        _gate.Wait();
        try
        {
            return Cache().TryGetValue(subject.Key, out var cached) && cached.ReaderVersion == HeadlessAdviceReader.Version
                ? cached.Advice
                : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyDictionary<HeadlessSubject, HeadlessAdvice>> ResolveAsync(
        IEnumerable<HeadlessSubject> subjects,
        IProgress<HeadlessAdviceResolved>? progress = null,
        CancellationToken ct = default)
    {
        var wanted = subjects.GroupBy(s => s.Key, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
        var resolved = new Dictionary<HeadlessSubject, HeadlessAdvice>();

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var cache = Cache();
            var stale = new List<HeadlessSubject>();

            foreach (var subject in wanted)
            {
                if (cache.TryGetValue(subject.Key, out var cached) && IsFresh(cached, subject))
                {
                    resolved[subject] = cached.Advice;
                    progress?.Report(new(subject, cached.Advice, FromCache: true));
                }
                else
                {
                    stale.Add(subject);
                }
            }

            var changed = false;
            try
            {
                for (var i = 0; i < stale.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    if (i > 0 && _pause > TimeSpan.Zero) await Task.Delay(_pause, ct).ConfigureAwait(false);

                    var subject = stale[i];
                    if (await FetchAsync(subject, ct).ConfigureAwait(false) is not { } fetched) continue;

                    cache[subject.Key] = fetched;
                    changed = true;
                    resolved[subject] = fetched.Advice;
                    progress?.Report(new(subject, fetched.Advice, FromCache: false));
                }
            }
            finally
            {
                if (changed) _store.Save(cache);
            }

            if (stale.Count > 0)
                AppLog.Info("HeadlessAdvice", $"{wanted.Count} subject(s): {wanted.Count - stale.Count} cached, {stale.Count} fetched or tried");

            return resolved;
        }
        finally
        {
            _gate.Release();
        }
    }

    private Dictionary<string, HeadlessAdviceStore.Cached> Cache() => _cache ??= _store.Load();

    private bool IsFresh(HeadlessAdviceStore.Cached cached, HeadlessSubject subject)
    {
        if (cached.ReaderVersion != HeadlessAdviceReader.Version) return false;

        return subject.UpdatedAt is { } updatedAt
            ? cached.UpdatedAt is { } stored && stored >= updatedAt
            : _now() - cached.CheckedAt < UnkeyedMaxAge;
    }

    private async Task<HeadlessAdviceStore.Cached?> FetchAsync(HeadlessSubject subject, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var (description, updatedAt) = await DescriptionAsync(subject, ct).ConfigureAwait(false);
                var advice = HeadlessAdviceReader.Read(description);

                return new HeadlessAdviceStore.Cached
                {
                    ReaderVersion = HeadlessAdviceReader.Version,
                    UpdatedAt = updatedAt,
                    CheckedAt = _now(),
                    Verdict = advice.Verdict,
                    Quotes = [.. advice.Quotes],
                };
            }
            catch (SpModApiRateLimitedException ex) when (attempt < RateLimitRetries)
            {
                await Task.Delay(ex.RetryAfter ?? TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
            }
            catch (SpModApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                AppLog.Debug("HeadlessAdvice", $"{subject.Key} is not listed on sp-mod");
                return null;
            }
            catch (Exception ex) when (ex is SpModApiException or HttpRequestException
                                           || (ex is TaskCanceledException && !ct.IsCancellationRequested))
            {
                AppLog.Warn("HeadlessAdvice", $"couldn't read the description of {subject.Key}: {ex.Message}");
                return null;
            }
        }
    }

    private async Task<(string? Description, DateTimeOffset? UpdatedAt)> DescriptionAsync(HeadlessSubject subject, CancellationToken ct)
    {
        const string fields = "id,description,updated_at";

        if (subject.Kind == HeadlessSubjectKind.Addon)
        {
            var addon = await _spModApi.GetAddonAsync(subject.Id.ToString(), fields: fields, ct: ct).ConfigureAwait(false);
            return (addon.Description, addon.UpdatedAt);
        }

        var mod = await _spModApi.GetModAsync(subject.Id.ToString(), fields: fields, ct: ct).ConfigureAwait(false);
        return (mod.Description, mod.UpdatedAt);
    }
}
