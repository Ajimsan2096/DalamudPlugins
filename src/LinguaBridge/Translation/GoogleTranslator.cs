using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using static LinguaBridge.Localization.Loc;

namespace LinguaBridge.Translation;

/// <summary>
/// Google Translate's public web endpoint. No API key needed, but it is unofficial and blocks heavy use.
/// To stay clear of that block it paces itself (a few requests per minute at most), and when Google answers
/// "too many requests" it pauses (2, 5, 10, 20, then 30 minutes), then checks by itself whether Google
/// answers again and resumes. Nothing has to be done by hand, with or without DeepL / AI as a backup.
/// </summary>
public sealed class GoogleTranslator : ITranslator
{
    /// <summary>Requests allowed in any 60 s window (normal chat stays far below this).</summary>
    private const int RequestsPerMinute = 25;

    /// <summary>How long a request may wait for a free slot before the message is skipped.</summary>
    private static readonly TimeSpan MaxSlotWait = TimeSpan.FromSeconds(4);

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan[] Backoff =
    {
        TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(30),
    };

    private readonly HttpClient http;
    private readonly Func<bool> inUse;
    private readonly CancellationToken lifetime;
    private readonly object gate = new();
    private readonly Queue<DateTime> recent = new();
    private DateTime pausedUntil = DateTime.MinValue;
    private int rateLimitStrikes;  // consecutive pauses for "too many requests"
    private int networkFailures;   // consecutive network errors / timeouts
    private bool wasPaused;
    private int probeGeneration;

    /// <param name="inUse">True while Google is the engine or a backup (only then is it worth checking it again).</param>
    public GoogleTranslator(HttpClient http, Func<bool> inUse, CancellationToken lifetime)
    {
        this.http = http;
        this.inUse = inUse;
        this.lifetime = lifetime;
    }

    public string Name => "Google";

    /// <summary>Raised when Google is paused (how long, why). From a worker thread.</summary>
    public event Action<TimeSpan, string>? Paused;

    /// <summary>Raised when Google answers again after a pause. From a worker thread.</summary>
    public event Action? Resumed;

    /// <summary>Time left in the current pause, or null when Google can be used.</summary>
    public TimeSpan? PauseRemaining
    {
        get
        {
            lock (gate)
            {
                var left = pausedUntil - DateTime.Now;
                return left > TimeSpan.Zero ? left : null;
            }
        }
    }

    /// <summary>Ends a pause now and checks Google right away (settings "Retry now").</summary>
    public void ResumeNow()
    {
        lock (gate)
        {
            pausedUntil = DateTime.MinValue;
            networkFailures = 0;
        }
        _ = ProbeAsync(Interlocked.Increment(ref probeGeneration));
    }

    public async Task<EngineResult> TranslateAsync(string text, Language? source, Language target, TranslateOptions options, CancellationToken ct)
    {
        _ = options; // Google has no context/glossary support
        // Protected spans (item links, names) travel as {0} placeholders so Google does not translate them; if a
        // placeholder comes back damaged, the plain text is sent instead. Text that already contains "{0}"-like
        // parts would be ambiguous: it goes as plain text straight away (one request, not two).
        if (ProtectedText.HasProtected(text) && !ProtectedText.HasPlaceholderLookalike(text))
        {
            var query = ProtectedText.ToPlaceholders(text, out var spans);
            var r = await RequestAsync(query, source, target, ct).ConfigureAwait(false);
            var restored = ProtectedText.FromPlaceholders(r.Text, spans);
            if (restored != null)
                return r with { Text = restored };
        }
        return await RequestAsync(ProtectedText.Plain(text), source, target, ct).ConfigureAwait(false);
    }

    private static string Wait(TimeSpan t) =>
        t.TotalMinutes >= 1 ? LF("{0} min", Math.Ceiling(t.TotalMinutes)) : LF("{0} s", Math.Max(1, Math.Ceiling(t.TotalSeconds)));

    /// <summary>Waits for a free request slot, or throws when paused / too busy.</summary>
    private async Task TakeSlotAsync(CancellationToken ct)
    {
        while (true)
        {
            TimeSpan wait;
            lock (gate)
            {
                var now = DateTime.Now;
                if (pausedUntil > now)
                    throw new EnginePausedException(
                        LF("Google is paused (too many requests), it resumes by itself in {0}", Wait(pausedUntil - now)),
                        pausedUntil - now);
                while (recent.Count > 0 && now - recent.Peek() >= TimeSpan.FromMinutes(1))
                    recent.Dequeue();
                if (recent.Count < RequestsPerMinute)
                {
                    recent.Enqueue(now);
                    return;
                }
                wait = recent.Peek() + TimeSpan.FromMinutes(1) - now;
            }
            if (wait > MaxSlotWait)
                throw new EnginePausedException(
                    LF("Too many messages for Google right now, this one was skipped (next slot in {0})", Wait(wait)), wait);
            await Task.Delay(wait, ct).ConfigureAwait(false);
        }
    }

    private async Task<EngineResult> RequestAsync(string query, Language? source, Language target, CancellationToken ct)
    {
        await TakeSlotAsync(ct).ConfigureAwait(false);
        try
        {
            var result = await SendAsync(query, source?.GoogleCode ?? "auto", target.GoogleCode, ct).ConfigureAwait(false);
            OnSuccess();
            return result;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException)
        {
            OnNetworkFailure();
            throw new TranslationException(L("Google did not respond (network problem or timeout)"));
        }
    }

    private async Task<EngineResult> SendAsync(string query, string sl, string tl, CancellationToken ct)
    {
        var url = $"https://translate.googleapis.com/translate_a/single?client=gtx&dt=t&sl={sl}&tl={tl}";
        var q = Uri.EscapeDataString(query);
        using var request = url.Length + q.Length + 3 <= 2000
            ? new HttpRequestMessage(HttpMethod.Get, url + "&q=" + q)
            // Long texts (e.g. a copied post in Japanese) go in the body: a URL that long is refused.
            : new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent("q=" + q, Encoding.UTF8, "application/x-www-form-urlencoded"),
            };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(RequestTimeout);
        using var response = await http.SendAsync(request, cts.Token).ConfigureAwait(false);
        var status = (int)response.StatusCode;
        if (status is 429 or 403 or 503)
            throw OnRateLimited();
        if (!response.IsSuccessStatusCode)
            throw new TranslationException(LF("Google error: HTTP {0}", status));

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false));
        var root = doc.RootElement;

        // Shape: [[["translated","original",...], ...], null, "en", ...]
        var sb = new StringBuilder();
        if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0 && root[0].ValueKind == JsonValueKind.Array)
        {
            foreach (var segment in root[0].EnumerateArray())
            {
                if (segment.ValueKind == JsonValueKind.Array && segment.GetArrayLength() > 0 &&
                    segment[0].ValueKind == JsonValueKind.String)
                    sb.Append(segment[0].GetString());
            }
        }

        var detected = root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 2 &&
                       root[2].ValueKind == JsonValueKind.String
            ? root[2].GetString()
            : null;

        if (sb.Length == 0)
            throw new TranslationException(L("Google returned an empty response."));

        return new EngineResult(sb.ToString(), Languages.NormalizeDetected(detected));
    }

    private void OnSuccess()
    {
        bool announce;
        lock (gate)
        {
            announce = wasPaused;
            wasPaused = false;
            rateLimitStrikes = 0;
            networkFailures = 0;
        }
        if (announce)
            Resumed?.Invoke();
    }

    private EnginePausedException OnRateLimited()
    {
        TimeSpan pause;
        lock (gate)
        {
            pause = Backoff[Math.Min(rateLimitStrikes, Backoff.Length - 1)];
            rateLimitStrikes++;
            pausedUntil = DateTime.Now + pause;
            wasPaused = true;
            recent.Clear();
        }
        Paused?.Invoke(pause, L("too many requests"));
        ScheduleProbe(pause);
        return new EnginePausedException(
            LF("Google is paused (too many requests), it resumes by itself in {0}", Wait(pause)), pause);
    }

    private void OnNetworkFailure()
    {
        TimeSpan? pause = null;
        lock (gate)
        {
            networkFailures++;
            // Three failures in a row: Google (or the connection) is down. Wait a minute instead of trying every message.
            if (networkFailures >= 3 && pausedUntil <= DateTime.Now)
            {
                pause = TimeSpan.FromMinutes(1);
                pausedUntil = DateTime.Now + pause.Value;
                wasPaused = true;
            }
        }
        if (pause is { } p)
        {
            Paused?.Invoke(p, L("no answer"));
            ScheduleProbe(p);
        }
    }

    private void ScheduleProbe(TimeSpan after)
    {
        var generation = Interlocked.Increment(ref probeGeneration);
        _ = Task.Delay(after + TimeSpan.FromSeconds(2), lifetime)
                .ContinueWith(t =>
                {
                    if (!t.IsCanceled)
                        _ = ProbeAsync(generation);
                }, TaskScheduler.Default);
    }

    /// <summary>After a pause: one tiny request to see whether Google answers again (only if Google is in use).</summary>
    private async Task ProbeAsync(int generation)
    {
        if (generation != Volatile.Read(ref probeGeneration) || lifetime.IsCancellationRequested)
            return;
        if (!inUse())
            return; // not used right now: the pause simply runs out, the next message tries again
        try
        {
            await RequestAsync("hello", Languages.Get("EN"), Languages.Get("FR"), lifetime).ConfigureAwait(false);
        }
        catch
        {
            // still blocked or offline: a new pause (and probe) was scheduled by the failure itself
        }
    }
}
