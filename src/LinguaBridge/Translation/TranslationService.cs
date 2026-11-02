using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace LinguaBridge.Translation;

public sealed record TranslationResult(string Original, string Text, Language Source, Language Target, string Engine);

/// <param name="History">Recent lines of the same conversation ("Name: text"), used as DeepL context.</param>
/// <param name="Names">Character names in play (sender, you) that must never be translated.</param>
public sealed record TranslateRequestContext(string? History = null, IReadOnlyList<string>? Names = null);

/// <summary>
/// Picks the direction (A to B or B to A), calls the selected engine (falling back to Google when
/// DeepL fails), protects names/links, and caches results so repeated messages cost nothing.
/// </summary>
public sealed class TranslationService : IDisposable
{
    private const int CacheCapacity = 512;

    /// <summary>RP shorthand kept as-is in roleplay mode.</summary>
    private static readonly string[] RoleplayShorthand = { "OOC", "IC", "RP", "ERP", "LRP", "MRP", "HRP", "AFK", "BRB" };

    private readonly Func<Configuration> config;
    private readonly HttpClient http;
    private readonly SemaphoreSlim concurrency = new(3);
    private readonly CancellationTokenSource lifetime = new();

    private readonly object cacheLock = new();
    private readonly Dictionary<string, LinkedListNode<(string Key, TranslationResult? Value)>> cacheMap = new();
    private readonly LinkedList<(string Key, TranslationResult? Value)> cacheOrder = new();

    public DeepLTranslator DeepL { get; }
    public GoogleTranslator Google { get; }

    /// <summary>Raised (from a worker thread) when DeepL failed and Google was used instead.</summary>
    public event Action<string>? FallbackUsed;

    /// <summary>Characters sent to the engine this session (cache hits not counted).</summary>
    public long CharactersSent => Interlocked.Read(ref charactersSent);
    private long charactersSent;

    public TranslationService(Func<Configuration> config)
    {
        this.config = config;
        http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("LinguaBridge-Dalamud/1.1");
        DeepL = new DeepLTranslator(http, config);
        Google = new GoogleTranslator(http);
    }

    private ITranslator Engine => config().Engine == TranslationEngine.DeepL ? DeepL : Google;

    /// <summary>
    /// Translates <paramref name="text"/> to the "other" language of the configured pair, or to
    /// <paramref name="forcedTarget"/> when given. Returns null when the text is already in the
    /// target language, or is in a language outside the pair and the settings say to ignore it.
    /// The text may contain <see cref="ProtectedText"/> spans.
    /// </summary>
    public async Task<TranslationResult?> TranslateAsync(
        string text, Language? forcedTarget = null, bool translateOthers = false, TranslateRequestContext? context = null)
    {
        var cfg = config();
        var a = Languages.Get(cfg.LanguageA);
        var b = Languages.Get(cfg.LanguageB);
        var preferred = Languages.Get(cfg.PreferredLanguage);
        var engine = Engine;

        var protectedTerms = new List<string>(cfg.ProtectedTerms);
        if (cfg.AutoProtectNames)
        {
            foreach (var name in context?.Names ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                protectedTerms.Add(name.Trim());
                // "Eliza Yevon" is often written as just "Eliza" in RP.
                protectedTerms.AddRange(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(p => p.Length >= 3));
            }
            if (cfg.RoleplayMode)
                protectedTerms.AddRange(RoleplayShorthand);
        }
        text = ProtectedText.ProtectTerms(text, protectedTerms);
        var options = new TranslateOptions(cfg.ContextLines > 0 ? context?.History : null, cfg.RoleplayMode);

        // Context is deliberately not part of the key: same line, same translation (and fewer billed characters).
        var key = $"{engine.Name}|{cfg.RoleplayMode}|{forcedTarget?.Code}|{a.Code}|{b.Code}|{preferred.Code}|{translateOthers}|{text}";
        if (TryGetCached(key, out var cached))
            return cached;

        await concurrency.WaitAsync(lifetime.Token).ConfigureAwait(false);
        try
        {
            var result = await TranslateUncachedAsync(engine, text, a, b, preferred, forcedTarget, translateOthers, options, lifetime.Token)
                             .ConfigureAwait(false);
            AddCached(key, result);
            return result;
        }
        finally
        {
            concurrency.Release();
        }
    }

    private async Task<TranslationResult?> TranslateUncachedAsync(
        ITranslator engine, string text, Language a, Language b, Language preferred, Language? forcedTarget,
        bool translateOthers, TranslateOptions options, CancellationToken ct)
    {
        var detectable = ProtectedText.WithoutProtected(text);

        if (forcedTarget != null)
        {
            var guess = LanguageDetector.Detect(detectable, new[] { a.Code, b.Code, forcedTarget.Code });
            if (guess == forcedTarget.Code)
                return null;
            var forced = await Make(engine, text, guess == null ? null : Languages.TryGet(guess), forcedTarget, options, ct);
            // Incoming one-way mode: a third language is only shown if the user asked for those.
            if (forced != null && !translateOthers && forced.Source.Code != a.Code && forced.Source.Code != b.Code)
                return null;
            return forced;
        }

        // 1) Cheap offline guess between the two languages of the pair.
        var detected = LanguageDetector.Detect(detectable, new[] { a.Code, b.Code });
        if (detected == a.Code)
            return await Make(engine, text, a, b, options, ct);
        if (detected == b.Code)
            return await Make(engine, text, b, a, options, ct);

        // 2) Unsure: ask the engine to translate to B with auto-detection and look at what it detected.
        var first = await CallAsync(engine, text, null, b, options, ct);
        if (first.Result.DetectedSource == b.Code)
            return await Make(engine, text, b, a, options, ct);

        var isA = first.Result.DetectedSource == a.Code;
        if (!isA && !translateOthers)
            return null; // a third language and the user chose to ignore those

        var source = Languages.TryGet(first.Result.DetectedSource) ?? Unknown(first.Result.DetectedSource);
        if (!isA && preferred.Code != b.Code)
        {
            if (source.Code == preferred.Code)
                return null;
            return await Make(engine, text, null, preferred, options, ct);
        }

        if (IsSame(first.Result.Text, text))
            return null; // names, emotes, untranslatable
        return new TranslationResult(ProtectedText.Plain(text), first.Result.Text, source, b, first.Engine);
    }

    private async Task<TranslationResult?> Make(
        ITranslator engine, string text, Language? source, Language target, TranslateOptions options, CancellationToken ct)
    {
        var input = source == null ? text : Prepare(text, source.Code);
        var (r, used) = await CallAsync(engine, input, source, target, options, ct);
        if (source == null && r.DetectedSource == target.Code)
            return null;
        if (IsSame(r.Text, text) || IsSame(r.Text, input))
            return null;
        var src = source ?? Languages.TryGet(r.DetectedSource) ?? Unknown(r.DetectedSource);
        return new TranslationResult(ProtectedText.Plain(text), r.Text, src, target, used);
    }

    private static bool IsSame(string translated, string original) =>
        string.Equals(translated.Trim(), ProtectedText.Plain(original).Trim(), StringComparison.OrdinalIgnoreCase);

    private string Prepare(string text, string languageCode) =>
        config().ExpandAbbreviations ? ProtectedText.MapUnprotected(text, part => Slang.Expand(part, languageCode)) : text;

    private async Task<(EngineResult Result, string Engine)> CallAsync(
        ITranslator engine, string text, Language? source, Language target, TranslateOptions options, CancellationToken ct)
    {
        Interlocked.Add(ref charactersSent, text.Length);
        try
        {
            return (await engine.TranslateAsync(text, source, target, options, ct).ConfigureAwait(false), engine.Name);
        }
        catch (Exception ex) when (engine == DeepL && config().FallbackToGoogle && !ct.IsCancellationRequested &&
                                   ex is TranslationException or HttpRequestException or TaskCanceledException)
        {
            FallbackUsed?.Invoke(ex is TranslationException ? ex.Message : "DeepL did not respond");
            return (await Google.TranslateAsync(text, source, target, options, ct).ConfigureAwait(false), "Google (fallback)");
        }
    }

    private static Language Unknown(string code) =>
        new(string.IsNullOrEmpty(code) ? "??" : code, code, code, code, code.ToLowerInvariant());

    private bool TryGetCached(string key, out TranslationResult? value)
    {
        lock (cacheLock)
        {
            if (cacheMap.TryGetValue(key, out var node))
            {
                cacheOrder.Remove(node);
                cacheOrder.AddFirst(node);
                value = node.Value.Value;
                return true;
            }
        }
        value = null;
        return false;
    }

    private void AddCached(string key, TranslationResult? value)
    {
        lock (cacheLock)
        {
            if (cacheMap.ContainsKey(key))
                return;
            var node = cacheOrder.AddFirst((key, value));
            cacheMap[key] = node;
            if (cacheOrder.Count > CacheCapacity)
            {
                var last = cacheOrder.Last!;
                cacheOrder.RemoveLast();
                cacheMap.Remove(last.Value.Key);
            }
        }
    }

    public void ClearCache()
    {
        lock (cacheLock)
        {
            cacheMap.Clear();
            cacheOrder.Clear();
        }
    }

    public void Dispose()
    {
        lifetime.Cancel();
        http.Dispose();
        concurrency.Dispose();
        lifetime.Dispose();
    }
}
