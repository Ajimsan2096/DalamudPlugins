using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace LinguaBridge.Translation;

public sealed record TranslationResult(string Original, string Text, Language Source, Language Target, string Engine);

/// <summary>
/// Picks the direction (A to B or B to A), calls the selected engine, and caches results so that
/// repeated messages (shouts, macros) cost nothing.
/// </summary>
public sealed class TranslationService : IDisposable
{
    private const int CacheCapacity = 512;

    private readonly Func<Configuration> config;
    private readonly HttpClient http;
    private readonly SemaphoreSlim concurrency = new(3);
    private readonly CancellationTokenSource lifetime = new();

    private readonly object cacheLock = new();
    private readonly Dictionary<string, LinkedListNode<(string Key, TranslationResult? Value)>> cacheMap = new();
    private readonly LinkedList<(string Key, TranslationResult? Value)> cacheOrder = new();

    public DeepLTranslator DeepL { get; }
    public GoogleTranslator Google { get; }

    /// <summary>Characters sent to the engine this session (cache hits not counted).</summary>
    public long CharactersSent => Interlocked.Read(ref charactersSent);
    private long charactersSent;

    public TranslationService(Func<Configuration> config)
    {
        this.config = config;
        http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("LinguaBridge-Dalamud/1.0");
        DeepL = new DeepLTranslator(http, config);
        Google = new GoogleTranslator(http);
    }

    private ITranslator Engine => config().Engine == TranslationEngine.DeepL ? DeepL : Google;

    /// <summary>
    /// Translates <paramref name="text"/> to the "other" language of the configured pair, or to
    /// <paramref name="forcedTarget"/> when given. Returns null when the text is already in the
    /// target language, or is in a language outside the pair and the settings say to ignore it.
    /// </summary>
    public async Task<TranslationResult?> TranslateAsync(string text, Language? forcedTarget = null, bool translateOthers = false)
    {
        var cfg = config();
        var a = Languages.Get(cfg.LanguageA);
        var b = Languages.Get(cfg.LanguageB);
        var engine = Engine;

        var key = $"{engine.Name}|{forcedTarget?.Code}|{a.Code}|{b.Code}|{translateOthers}|{text}";
        if (TryGetCached(key, out var cached))
            return cached;

        await concurrency.WaitAsync(lifetime.Token).ConfigureAwait(false);
        try
        {
            var result = await TranslateUncachedAsync(engine, text, a, b, forcedTarget, translateOthers, lifetime.Token)
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
        ITranslator engine, string text, Language a, Language b, Language? forcedTarget, bool translateOthers, CancellationToken ct)
    {
        if (forcedTarget != null)
        {
            var guess = LanguageDetector.Detect(text, new[] { a.Code, b.Code, forcedTarget.Code });
            if (guess == forcedTarget.Code)
                return null;
            var input = guess == null ? text : Prepare(text, guess);
            var r = await CallAsync(engine, input, guess == null ? null : Languages.TryGet(guess), forcedTarget, ct);
            if (r.DetectedSource == forcedTarget.Code)
                return null;
            return new TranslationResult(text, r.Text, Languages.TryGet(r.DetectedSource) ?? Unknown(r.DetectedSource), forcedTarget, engine.Name);
        }

        // 1) Cheap offline guess between the two languages of the pair.
        var detected = LanguageDetector.Detect(text, new[] { a.Code, b.Code });
        if (detected == a.Code)
            return await Make(engine, text, a, b, ct);
        if (detected == b.Code)
            return await Make(engine, text, b, a, ct);

        // 2) Unsure: ask the engine to translate to B with auto-detection and look at what it detected.
        var first = await CallAsync(engine, text, null, b, ct);
        if (first.DetectedSource == b.Code)
            return await Make(engine, text, b, a, ct);
        if (first.DetectedSource == a.Code || translateOthers)
        {
            if (string.Equals(first.Text.Trim(), text.Trim(), StringComparison.OrdinalIgnoreCase))
                return null; // names, emotes, untranslatable
            var src = Languages.TryGet(first.DetectedSource) ?? Unknown(first.DetectedSource);
            return new TranslationResult(text, first.Text, src, b, engine.Name);
        }

        return null; // a third language and the user chose to ignore those
    }

    private async Task<TranslationResult?> Make(ITranslator engine, string text, Language source, Language target, CancellationToken ct)
    {
        var input = Prepare(text, source.Code);
        var r = await CallAsync(engine, input, source, target, ct);
        if (string.Equals(r.Text.Trim(), text.Trim(), StringComparison.OrdinalIgnoreCase) ||
            string.Equals(r.Text.Trim(), input.Trim(), StringComparison.OrdinalIgnoreCase))
            return null;
        return new TranslationResult(text, r.Text, source, target, engine.Name);
    }

    private string Prepare(string text, string languageCode) =>
        config().ExpandAbbreviations ? Slang.Expand(text, languageCode) : text;

    private async Task<EngineResult> CallAsync(ITranslator engine, string text, Language? source, Language target, CancellationToken ct)
    {
        Interlocked.Add(ref charactersSent, text.Length);
        return await engine.TranslateAsync(text, source, target, ct).ConfigureAwait(false);
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
