using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using static LinguaBridge.Localization.Loc;

namespace LinguaBridge.Translation;

public sealed record TranslationResult(string Original, string Text, Language Source, Language Target, string Engine);

/// <param name="History">Recent lines of the same conversation ("Name: text"), used as DeepL context.</param>
/// <param name="Names">Character names in play (sender, you) that must never be translated.</param>
public sealed record TranslateRequestContext(string? History = null, IReadOnlyList<string>? Names = null);

/// <summary>
/// Which source languages incoming chat may be translated from: every language except the ones the user already
/// understands, or only a chosen few.
/// </summary>
public sealed class LanguageFilter
{
    private readonly HashSet<string> skip;
    private readonly HashSet<string>? only;

    /// <param name="skip">Never translated (languages the user understands).</param>
    /// <param name="only">When not null, only these are translated.</param>
    public LanguageFilter(IEnumerable<string> skip, IEnumerable<string>? only)
    {
        this.skip = new HashSet<string>(skip.Select(s => s.ToUpperInvariant()));
        this.only = only == null ? null : new HashSet<string>(only.Select(s => s.ToUpperInvariant()));
        Key = (this.only == null ? "x:" + string.Join(",", this.skip.OrderBy(s => s)) : "o:" + string.Join(",", this.only.OrderBy(s => s)));
    }

    /// <summary>Stable text form, for the translation cache.</summary>
    public string Key { get; }

    /// <summary>True when a message written in <paramref name="code"/> may be translated. Unknown languages count as
    /// "other": translated in "all except" mode, not in "only these" mode.</summary>
    public bool Allows(string? code)
    {
        var c = string.IsNullOrWhiteSpace(code) || code == "??" ? null : code.ToUpperInvariant();
        if (only != null)
            return c != null && only.Contains(c) && !skip.Contains(c);
        return c == null || !skip.Contains(c);
    }
}

/// <summary>
/// Picks the direction (A to B or B to A), calls the selected engine (falling back to the others when it fails
/// or is paused), protects names/links, and caches results so repeated messages cost nothing.
/// </summary>
public sealed class TranslationService : IDisposable
{
    private const int CacheCapacity = 1024;

    /// <summary>RP shorthand kept as-is in roleplay mode.</summary>
    private static readonly string[] RoleplayShorthand = { "OOC", "IC", "RP", "ERP", "LRP", "MRP", "HRP", "AFK", "BRB" };

    /// <summary>
    /// Gaming acronyms that are not words in any language: always kept as typed (engines turn "RIP" into
    /// "QEPD", "GG WP" into nonsense...). "RIP" only in capitals, so the English verb "rip" is still translated.
    /// </summary>
    private static readonly string[] GamingAcronyms =
        { "GG", "GGs", "GGWP", "GGEZ", "WP", "GJ", "GLHF", "AFK", "BRB", "BBL", "BBIAB", "LFG", "LFM", "LMAO", "LMFAO", "ROFL", "XD" };
    private static readonly string[] CaseSensitiveAcronyms = { "RIP" };

    private static readonly string[] AllCodes = Languages.All.Select(l => l.Code).ToArray();

    private readonly Func<Configuration> config;
    private readonly HttpClient http;
    private readonly SemaphoreSlim concurrency = new(3);
    private readonly CancellationTokenSource lifetime = new();

    private readonly object cacheLock = new();
    private readonly Dictionary<string, LinkedListNode<(string Key, TranslationResult? Value)>> cacheMap = new();
    private readonly LinkedList<(string Key, TranslationResult? Value)> cacheOrder = new();
    /// <summary>Requests in flight: the same line arriving twice at once (macros, "congrats!") is sent only once.</summary>
    private readonly ConcurrentDictionary<string, Task<TranslationResult?>> inflight = new();

    public DeepLTranslator DeepL { get; }
    public GoogleTranslator Google { get; }
    public LlmTranslator Llm { get; }

    /// <summary>Raised (from a worker thread) when the main engine failed and a backup was used instead.</summary>
    public event Action<string>? FallbackUsed;

    /// <summary>Characters sent to the engine this session (cache hits not counted).</summary>
    public long CharactersSent => Interlocked.Read(ref charactersSent);
    private long charactersSent;

    /// <summary>Requests sent to the engines this session (cache hits not counted).</summary>
    public long RequestsSent => Interlocked.Read(ref requestsSent);
    private long requestsSent;

    /// <param name="handler">Only for offline tests (a fake engine); null in the plugin.</param>
    public TranslationService(Func<Configuration> config, HttpMessageHandler? handler = null)
    {
        this.config = config;
        // No global timeout: each engine sets its own (Google / DeepL 10 s, AI 30 s).
        http = handler == null ? new HttpClient() : new HttpClient(handler);
        http.Timeout = Timeout.InfiniteTimeSpan;
        http.DefaultRequestHeaders.UserAgent.ParseAdd("LinguaBridge-Dalamud/1.16");
        DeepL = new DeepLTranslator(http, config);
        Google = new GoogleTranslator(http, () => GoogleInUse, lifetime.Token);
        Llm = new LlmTranslator(http, config);
    }

    private ITranslator BaseEngine => config().Engine == TranslationEngine.DeepL ? DeepL : Google;

    /// <summary>True when Google is the engine, or the backup of DeepL / AI.</summary>
    public bool GoogleInUse => BaseEngine == Google || config().FallbackToGoogle;

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

    /// <summary>Short name of what translates right now ("AI", "DeepL", "Google").</summary>
    public string ActiveEngineName => UsingLlm ? "AI" : BaseEngine.Name;

    private ITranslator Engine => UsingLlm ? Llm : BaseEngine;

    /// <summary>
    /// Translates <paramref name="text"/> to the "other" language of the configured pair, or to
    /// <paramref name="forcedTarget"/> when given. Returns null when the text is already in the
    /// target language, or is written in a language <paramref name="filter"/> says not to translate.
    /// The text may contain <see cref="ProtectedText"/> spans.
    /// </summary>
    /// <param name="filter">Incoming chat only: the source languages the user wants translated. Null = any.</param>
    public Task<TranslationResult?> TranslateAsync(
        string text, Language? forcedTarget = null, LanguageFilter? filter = null, TranslateRequestContext? context = null)
    {
        var cfg = config();
        var a = Languages.Get(cfg.LanguageA);
        var b = Languages.Get(cfg.LanguageB);
        var preferred = Languages.Get(cfg.PreferredLanguage);
        var engine = Engine;

        // Only jargon / interjections ("RIP", "yep", "gg wp", "yush", "jajaja"): already readable, nothing to translate.
        if (LanguageDetector.IsTrivial(ProtectedText.WithoutProtected(text)))
            return Task.FromResult<TranslationResult?>(null);

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
        protectedTerms.AddRange(GamingAcronyms);
        text = ProtectedText.ProtectGameMacros(text);
        text = ProtectedText.ProtectTerms(text, protectedTerms);
        text = ProtectedText.ProtectTerms(text, CaseSensitiveAcronyms, ignoreCase: false);
        var options = new TranslateOptions(cfg.ContextLines > 0 ? context?.History : null, cfg.RoleplayMode,
                                           DescribeCharacters(cfg, context));

        // Context is deliberately not part of the key: same line, same translation (and fewer billed characters).
        var key = $"{engine.Name}|{cfg.RoleplayMode}|{forcedTarget?.Code}|{a.Code}|{b.Code}|{preferred.Code}|{filter?.Key}|{text}";
        if (TryGetCached(key, out var cached))
            return Task.FromResult(cached);

        var task = inflight.GetOrAdd(key, k => RunAsync(k, engine, text, a, b, preferred, forcedTarget, filter, options));
        return task;
    }

    private async Task<TranslationResult?> RunAsync(
        string key, ITranslator engine, string text, Language a, Language b, Language preferred, Language? forcedTarget,
        LanguageFilter? filter, TranslateOptions options)
    {
        await Task.Yield(); // never run inside GetOrAdd
        try
        {
            await concurrency.WaitAsync(lifetime.Token).ConfigureAwait(false);
            try
            {
                var result = await TranslateUncachedAsync(engine, text, a, b, preferred, forcedTarget, filter, options, lifetime.Token)
                                 .ConfigureAwait(false);
                AddCached(key, result);
                return result;
            }
            finally
            {
                concurrency.Release();
            }
        }
        finally
        {
            inflight.TryRemove(key, out _);
        }
    }

    private async Task<TranslationResult?> TranslateUncachedAsync(
        ITranslator engine, string text, Language a, Language b, Language preferred, Language? forcedTarget,
        LanguageFilter? filter, TranslateOptions options, CancellationToken ct)
    {
        var detectable = ProtectedText.WithoutProtected(text);

        // Messages that mix languages ("Spanish speech *English action* Spanish") are handled chunk by chunk.
        var chunks = MixedText.Split(text, new[] { a.Code, b.Code, forcedTarget?.Code ?? b.Code }.Distinct().ToArray());
        if (MixedText.IsMixed(chunks))
            return await TranslateMixedAsync(engine, text, chunks, a, b, forcedTarget, filter, options, ct);

        if (forcedTarget != null)
        {
            var guess = LanguageDetector.Detect(detectable, new[] { a.Code, b.Code, forcedTarget.Code });
            // Russian translit is unreadable for the engine's own detection: name the source ourselves.
            if (guess == null && forcedTarget.Code != "RU" && LanguageDetector.LooksRussian(detectable))
                guess = "RU";
            // Not one of the user's languages: a clear guess among all languages still saves a request when the
            // user does not want that language translated.
            if (guess == null && filter != null)
                guess = LanguageDetector.Detect(detectable, AllCodes);
            if (guess == forcedTarget.Code)
                return null;
            if (guess != null && filter != null && !filter.Allows(guess))
                return null; // decided offline: nothing sent
            var forced = await Make(engine, text, guess == null ? null : Languages.TryGet(guess), forcedTarget, options, ct);
            if (forced != null && filter != null && !filter.Allows(forced.Source.Code))
                return null;
            return forced;
        }

        // 1) Cheap offline guess between the two languages of the pair.
        var detected = LanguageDetector.Detect(detectable, new[] { a.Code, b.Code });
        if (detected == a.Code)
            return filter == null || filter.Allows(a.Code) ? await Make(engine, text, a, b, options, ct) : null;
        if (detected == b.Code)
            return filter == null || filter.Allows(b.Code) ? await Make(engine, text, b, a, options, ct) : null;

        // Russian outside the pair, typed in Latin letters: engines cannot detect translit, so say it is Russian.
        if (LanguageDetector.LooksRussian(detectable))
            return preferred.Code == "RU" || (filter != null && !filter.Allows("RU"))
                ? null
                : await Make(engine, text, Languages.Get("RU"), preferred, options, ct);

        // A clear third language: straight to the language the user reads best (one request, or none if unwanted).
        var local = LanguageDetector.Detect(detectable, AllCodes);
        if (local != null && local != a.Code && local != b.Code)
        {
            if (local == preferred.Code || (filter != null && !filter.Allows(local)))
                return null;
            return await Make(engine, text, Languages.Get(local), preferred, options, ct);
        }

        // 2) Unsure: one request with auto-detection, aimed at the most likely target (the language you read best
        // covers messages in the other pair language and in third languages alike). A second request is only
        // needed when the message turns out to be in that very language.
        var firstTarget = preferred.Code == a.Code || preferred.Code == b.Code ? preferred : b;
        var first = await CallAsync(engine, text, null, firstTarget, options, ct);
        var src = first.Result.DetectedSource;
        if (filter != null && !filter.Allows(src))
            return null;
        var desired = src == a.Code ? b : src == b.Code ? a : preferred;
        if (src == desired.Code)
            return null;
        if (desired.Code == firstTarget.Code)
        {
            if (IsSame(first.Result.Text, text))
                return null; // names, emotes, untranslatable
            var source = Languages.TryGet(src) ?? Unknown(src);
            return new TranslationResult(ProtectedText.Plain(text), first.Result.Text, source, firstTarget, first.Engine);
        }
        return await Make(engine, text, Languages.TryGet(src), desired, options, ct);
    }

    private async Task<TranslationResult?> TranslateMixedAsync(
        ITranslator engine, string text, List<MixedText.Chunk> chunks, Language a, Language b, Language? forcedTarget,
        LanguageFilter? filter, TranslateOptions options, CancellationToken ct)
    {
        var dominant = MixedText.Dominant(chunks);
        var target = forcedTarget ?? (dominant == a.Code ? b : a);
        var langs = chunks.Select(c => c.Lang).Where(l => l != null).Distinct().ToList();
        if (langs.All(l => l == target.Code))
            return null;
        // Parts the user does not want translated (already in target, or a language they understand) stay as written.
        bool Keep(MixedText.Chunk c) => c.Lang == target.Code || (filter != null && c.Lang != null && !filter.Allows(c.Lang));
        if (chunks.All(Keep))
            return null;
        var source = Languages.TryGet(chunks.Where(c => !Keep(c))
                                            .GroupBy(c => c.Lang).OrderByDescending(g => g.Sum(c => c.Text.Length))
                                            .First().Key ?? string.Empty) ?? Unknown("??");

        // AI understands mixed input in one go (and keeps the scene coherent).
        if (engine == Llm)
        {
            var (r, used) = await CallAsync(engine, text, null, target, options, ct);
            return IsSame(r.Text, text) ? null : new TranslationResult(ProtectedText.Plain(text), r.Text, source, target, used);
        }

        // DeepL / Google: one request per source language (its chunks joined by line breaks), not one per chunk.
        var translated = new string?[chunks.Count];
        var engines = new HashSet<string>();
        foreach (var group in chunks.Select((c, i) => (Chunk: c, Index: i)).Where(x => !Keep(x.Chunk)).GroupBy(x => x.Chunk.Lang))
        {
            var src = group.Key == null ? null : Languages.TryGet(group.Key);
            var items = group.ToList();
            var inputs = items.Select(x => src == null ? x.Chunk.Text : Prepare(x.Chunk.Text, src.Code)).ToList();
            if (items.Count > 1 && inputs.All(s => s.IndexOf('\n') < 0))
            {
                var (r, used) = await CallAsync(engine, string.Join("\n", inputs), src, target, options, ct);
                var lines = r.Text.Split('\n');
                if (lines.Length == items.Count)
                {
                    engines.Add(used);
                    for (var k = 0; k < items.Count; k++)
                        translated[items[k].Index] = lines[k].Trim();
                    continue;
                }
            }
            for (var k = 0; k < items.Count; k++)
            {
                var (r, used) = await CallAsync(engine, inputs[k], src, target, options, ct);
                engines.Add(used);
                translated[items[k].Index] = r.Text.Trim();
            }
        }

        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < chunks.Count; i++)
            sb.Append(translated[i] ?? ProtectedText.Plain(chunks[i].Text)).Append(chunks[i].Trailing);
        var result = sb.ToString().Trim();
        return IsSame(result, text)
            ? null
            : new TranslationResult(ProtectedText.Plain(text), result, source, target, string.Join(", ", engines));
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

    private string Prepare(string text, string languageCode)
    {
        // Russian typed in Latin letters ("privet, kak dela") is turned back into Cyrillic: engines read it far better.
        if (languageCode == "RU")
            text = ProtectedText.MapUnprotected(text, Translit.ToCyrillic);
        return config().ExpandAbbreviations ? ProtectedText.MapUnprotected(text, part => Slang.Expand(part, languageCode)) : text;
    }

    private async Task<(EngineResult Result, string Engine)> CallAsync(
        ITranslator engine, string text, Language? source, Language target, TranslateOptions options, CancellationToken ct)
    {
        Interlocked.Add(ref charactersSent, text.Length);

        // Chain: AI providers -> DeepL (if it is the main engine) -> Google (if fallback is on).
        // With Google as the main engine, a DeepL key or an AI provider (if any) covers Google's pauses.
        var chain = new List<ITranslator> { engine };
        if (engine == Llm && BaseEngine == DeepL && DeepL.Keys.Count > 0)
            chain.Add(DeepL);
        if (engine != Google && (config().FallbackToGoogle || BaseEngine == Google))
            chain.Add(Google);
        if (engine == Google)
        {
            if (DeepL.Keys.Count > 0)
                chain.Add(DeepL);
            if (Llm.ActiveProviders.Count > 0)
                chain.Add(Llm);
        }

        Exception? last = null;
        for (var i = 0; i < chain.Count; i++)
        {
            var current = chain[i];
            try
            {
                if (current != Google || Google.PauseRemaining == null)
                    Interlocked.Increment(ref requestsSent);
                var r = await current.TranslateAsync(text, source, target, options, ct).ConfigureAwait(false);
                var name = r.Engine ?? current.Name;
                if (i > 0)
                    name += " " + L("(fallback)");
                return (r, name);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && i < chain.Count - 1 &&
                                       ex is TranslationException or HttpRequestException or TaskCanceledException)
            {
                last = ex;
                // A Google pause is announced once by itself; no extra "backup engine" notice for every message.
                if (ex is not EnginePausedException)
                    FallbackUsed?.Invoke(ex is TranslationException ? ex.Message : LF("{0} did not respond", current.Name));
            }
        }
        throw last ?? new TranslationException(L("Translation failed"));
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
                        PlayerNames.ContainsWord(history, c.Name.Trim().Split(' ')[0]))
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
        // Cancel only: requests still waiting are released by the cancellation. Disposing the semaphore (or the
        // token source) here would make them fail with ObjectDisposedException while the plugin unloads.
        lifetime.Cancel();
        http.CancelPendingRequests();
    }
}
