using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace LinguaBridge.Translation;

/// <summary>
/// DeepL REST API v2. Free keys end in ":fx" and use api-free.deepl.com. Several keys can be configured:
/// when one runs out of monthly quota (HTTP 456) or is invalid, the next one is used for the same message.
/// </summary>
public sealed class DeepLTranslator : ITranslator
{
    private const string GameContext =
        "Casual chat message between players in the online game Final Fantasy XIV. " +
        "Players use internet slang and SMS abbreviations (e.g. French jsp = je sais pas, tkt = t'inquiète, mdr = lol). " +
        "Keep it short and informal; keep player names, game abbreviations and emoticons unchanged.";

    private readonly HttpClient http;
    private readonly Func<Configuration> config;
    private readonly object stateLock = new();
    private readonly Dictionary<string, KeyState> states = new();
    private int current; // index of the key that worked last; tried first

    /// <summary>Raised when a key stops working and the next one takes over (index, reason).</summary>
    public event Action<int, string>? KeyUnavailable;

    public DeepLTranslator(HttpClient http, Func<Configuration> config)
    {
        this.http = http;
        this.config = config;
    }

    public string Name => "DeepL";

    private sealed class KeyState
    {
        public DateTime BlockedUntil = DateTime.MinValue;
        public string Status = "not used yet";
        public (long Used, long Limit)? Usage;
    }

    public IReadOnlyList<string> Keys =>
        config().DeepLApiKeys.Select(k => k.Trim()).Where(k => k.Length > 0).Distinct().ToList();

    private static string BaseUrl(string key) =>
        key.Trim().EndsWith(":fx", StringComparison.Ordinal) ? "https://api-free.deepl.com" : "https://api.deepl.com";

    public static string Mask(string key) =>
        key.Length <= 10 ? "****" : $"{key[..4]}...{key[^6..]}";

    private KeyState State(string key)
    {
        lock (stateLock)
        {
            if (!states.TryGetValue(key, out var s))
                states[key] = s = new KeyState();
            return s;
        }
    }

    /// <summary>Status line for the settings UI.</summary>
    public string Describe(string key)
    {
        var s = State(key);
        var usage = s.Usage is { } u ? $"{u.Used:N0} / {u.Limit:N0} chars" : null;
        var blocked = s.BlockedUntil > DateTime.Now ? " (skipped for now)" : string.Empty;
        return usage == null ? s.Status + blocked : $"{s.Status}{blocked}, {usage}";
    }

    /// <summary>Forget "used up / invalid" marks, e.g. after editing keys or at the start of a month.</summary>
    public void ResetKeyStates()
    {
        lock (stateLock)
            states.Clear();
        current = 0;
    }

    public async Task<EngineResult> TranslateAsync(string text, Language? source, Language target, CancellationToken ct)
    {
        var keys = Keys;
        if (keys.Count == 0)
            throw new TranslationException("No DeepL API key set (/lb config).");

        var start = current < keys.Count ? current : 0;
        string? lastError = null;
        for (var n = 0; n < keys.Count; n++)
        {
            var index = (start + n) % keys.Count;
            var key = keys[index];
            var state = State(key);
            if (state.BlockedUntil > DateTime.Now)
                continue;

            var (result, status, error) = await TryKeyAsync(key, text, source, target, ct).ConfigureAwait(false);
            if (result != null)
            {
                state.Status = "OK";
                state.BlockedUntil = DateTime.MinValue;
                current = index;
                return result;
            }

            // Decide whether this key is worth retrying later, and move on to the next one.
            var blockFor = status switch
            {
                456 => TimeSpan.FromHours(6),    // monthly quota used up
                403 or 401 => TimeSpan.FromHours(24), // wrong / revoked key
                429 => TimeSpan.FromSeconds(30), // rate limited
                _ => TimeSpan.Zero,
            };
            lastError = error;
            if (blockFor == TimeSpan.Zero)
                throw new TranslationException($"DeepL error: {error}"); // network/server issue: other keys won't help
            state.BlockedUntil = DateTime.Now + blockFor;
            state.Status = error;
            if (keys.Count > 1)
                KeyUnavailable?.Invoke(index, error);
        }

        throw new TranslationException(keys.Count == 1
            ? $"DeepL error: {lastError ?? State(keys[0]).Status}"
            : "DeepL error: all API keys are used up or invalid");
    }

    private async Task<(EngineResult? Result, int Status, string Error)> TryKeyAsync(
        string key, string text, Language? source, Language target, CancellationToken ct)
    {
        var cfg = config();
        var hasProtected = ProtectedText.HasProtected(text);
        var body = new Dictionary<string, object>
        {
            ["text"] = new[] { hasProtected ? ProtectedText.ToDeepLXml(text) : text },
            ["target_lang"] = target.Code == "EN" ? cfg.EnglishVariant : target.DeepLTarget,
            ["context"] = GameContext,
        };
        if (source != null)
            body["source_lang"] = source.DeepLSource;
        if (hasProtected)
        {
            body["tag_handling"] = "xml";
            body["ignore_tags"] = new[] { "x" };
        }
        if (cfg.DeepLFormality != "default")
            body["formality"] = cfg.DeepLFormality;

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl(key)}/v2/translate")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.TryAddWithoutValidation("Authorization", $"DeepL-Auth-Key {key}");

        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        var status = (int)response.StatusCode;
        if (!response.IsSuccessStatusCode)
        {
            var reason = status switch
            {
                401 or 403 => "invalid API key",
                429 => "too many requests",
                456 => "monthly quota used up",
                _ => $"HTTP {status}",
            };
            return (null, status, reason);
        }

        var result = await response.Content.ReadFromJsonAsync<DeepLResponse>(cancellationToken: ct).ConfigureAwait(false);
        if (result?.Translations is not { Length: > 0 } t)
            return (null, 0, "empty response");

        var translated = hasProtected ? ProtectedText.FromDeepLXml(t[0].Text) : t[0].Text;
        return (new EngineResult(translated, Languages.NormalizeDetected(t[0].DetectedSourceLanguage)), status, string.Empty);
    }

    /// <summary>Returns (used, limit) characters for the current billing period of one key.</summary>
    public async Task<(long Used, long Limit)> GetUsageAsync(string key, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl(key)}/v2/usage");
        request.Headers.TryAddWithoutValidation("Authorization", $"DeepL-Auth-Key {key}");
        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        var state = State(key);
        if (!response.IsSuccessStatusCode)
        {
            state.Status = (int)response.StatusCode is 401 or 403 ? "invalid API key" : $"HTTP {(int)response.StatusCode}";
            throw new TranslationException($"DeepL usage error: {state.Status}");
        }
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        var usage = (doc.RootElement.GetProperty("character_count").GetInt64(),
                     doc.RootElement.GetProperty("character_limit").GetInt64());
        state.Usage = usage;
        if (usage.Item1 >= usage.Item2)
        {
            state.Status = "monthly quota used up";
            state.BlockedUntil = DateTime.Now + TimeSpan.FromHours(6);
        }
        else
        {
            state.Status = "OK";
            state.BlockedUntil = DateTime.MinValue;
        }
        return usage;
    }

    /// <summary>Refreshes usage of every key (settings "Check usage" button).</summary>
    public async Task RefreshAllUsageAsync(CancellationToken ct)
    {
        foreach (var key in Keys)
        {
            try
            {
                await GetUsageAsync(key, ct).ConfigureAwait(false);
            }
            catch (TranslationException)
            {
                // status already recorded
            }
        }
    }

    private sealed class DeepLResponse
    {
        [JsonPropertyName("translations")]
        public DeepLTranslation[]? Translations { get; set; }
    }

    private sealed class DeepLTranslation
    {
        [JsonPropertyName("detected_source_language")]
        public string? DetectedSourceLanguage { get; set; }

        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;
    }
}

public sealed class TranslationException(string message) : Exception(message);
