using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
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
    public LlmTranslator Llm { get; }

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
        Llm = new LlmTranslator(http, config);
    }

    private ITranslator BaseEngine => config().Engine == TranslationEngine.DeepL ? DeepL : Google;

    /// <summary>True when the AI providers are used for the current mode.</summary>
    public bool UsingLlm
    {
        get
        {
            var cfg = config();
            var wanted = cfg.LlmUsage == LlmUsage.Always || (cfg.LlmUsage == LlmUsage.RoleplayOnly && cfg.RoleplayMode);
            return wanted && Llm.ActiveProviders.Count > 0;
        }
    }

    private ITranslator Engine => UsingLlm ? Llm : BaseEngine;

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
        var options = new TranslateOptions(cfg.ContextLines > 0 ? context?.History : null, cfg.RoleplayMode,
                                           DescribeCharacters(cfg, context));

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

        // Messages that mix languages ("Spanish speech *English action* Spanish") are handled chunk by chunk.
        var chunks = MixedText.Split(text);
        if (MixedText.IsMixed(chunks))
            return await TranslateMixedAsync(engine, text, chunks, a, b, forcedTarget, translateOthers, options, ct);

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

    private async Task<TranslationResult?> TranslateMixedAsync(
        ITranslator engine, string text, List<MixedText.Chunk> chunks, Language a, Language b, Language? forcedTarget,
        bool translateOthers, TranslateOptions options, CancellationToken ct)
    {
        var dominant = MixedText.Dominant(chunks);
        var target = forcedTarget ?? (dominant == a.Code ? b : a);
        var langs = chunks.Select(c => c.Lang).Where(l => l != null).Distinct().ToList();
        if (langs.All(l => l == target.Code))
            return null;
        if (!translateOthers && !langs.Any(l => l != target.Code && (l == a.Code || l == b.Code)))
            return null;
        var source = Languages.TryGet(chunks.Where(c => c.Lang != target.Code)
                                            .GroupBy(c => c.Lang).OrderByDescending(g => g.Sum(c => c.Text.Length))
                                            .First().Key ?? string.Empty) ?? Unknown("??");

        // AI understands mixed input in one go (and keeps the scene coherent).
        if (engine == Llm)
        {
            var (r, used) = await CallAsync(engine, text, null, target, options, ct);
            return IsSame(r.Text, text) ? null : new TranslationResult(ProtectedText.Plain(text), r.Text, source, target, used);
        }

        // DeepL / Google: one request per language chunk, each from its real source language.
        var sb = new StringBuilder();
        var engines = new HashSet<string>();
        foreach (var chunk in chunks)
        {
            if (chunk.Lang == target.Code)
            {
                sb.Append(ProtectedText.Plain(chunk.Text)).Append(chunk.Trailing);
                continue;
            }
            var src = chunk.Lang == null ? null : Languages.TryGet(chunk.Lang);
            var input = src == null ? chunk.Text : Prepare(chunk.Text, src.Code);
            var (r, used) = await CallAsync(engine, input, src, target, options, ct);
            engines.Add(used);
            sb.Append(r.Text.Trim()).Append(chunk.Trailing);
        }
        var translated = sb.ToString().Trim();
        return IsSame(translated, text)
            ? null
            : new TranslationResult(ProtectedText.Plain(text), translated, source, target, string.Join(", ", engines));
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

        // Chain: AI providers -> DeepL (if it is the main engine) -> Google (if fallback is on).
        var chain = new List<ITranslator> { engine };
        if (engine == Llm && BaseEngine == DeepL && DeepL.Keys.Count > 0)
            chain.Add(DeepL);
        if (engine != Google && (config().FallbackToGoogle || BaseEngine == Google))
            chain.Add(Google);

        Exception? last = null;
        for (var i = 0; i < chain.Count; i++)
        {
            var current = chain[i];
            try
            {
                var r = await current.TranslateAsync(text, source, target, options, ct).ConfigureAwait(false);
                var name = current == Llm ? Llm.LastProvider : current.Name;
                if (i > 0)
                    name += " (fallback)";
                return (r, name);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && i < chain.Count - 1 &&
                                       ex is TranslationException or HttpRequestException or TaskCanceledException)
            {
                last = ex;
                FallbackUsed?.Invoke(ex is TranslationException ? ex.Message : $"{current.Name} did not respond");
            }
        }
        throw last ?? new TranslationException("Translation failed");
    }

    /// <summary>
    /// Profiles of the characters involved: the names passed in (sender, you, tell partner) plus anyone
    /// whose first name appears in the recent conversation (e.g. the rest of a party scene).
    /// </summary>
    private static string? DescribeCharacters(Configuration cfg, TranslateRequestContext? context)
    {
        if (cfg.Characters.Count == 0)
            return null;
        var names = context?.Names ?? Array.Empty<string>();
        var history = context?.History ?? string.Empty;
        var lines = cfg.Characters
            .Where(c => !string.IsNullOrWhiteSpace(c.Name))
            .Where(c => names.Any(n => n.Equals(c.Name.Trim(), StringComparison.OrdinalIgnoreCase) ||
                                       n.StartsWith(c.Name.Trim() + " ", StringComparison.OrdinalIgnoreCase)) ||
                        history.Contains(c.Name.Trim().Split(' ')[0], StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Describe())
            .ToList();
        return lines.Count == 0 ? null : string.Join("\n", lines);
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
