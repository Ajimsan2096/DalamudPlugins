using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace LinguaBridge.Translation;

/// <summary>DeepL REST API v2. Free keys end in ":fx" and use api-free.deepl.com.</summary>
public sealed class DeepLTranslator : ITranslator
{
    private const string GameContext =
        "Casual chat message between players in the online game Final Fantasy XIV. " +
        "Players use internet slang and SMS abbreviations (e.g. French jsp = je sais pas, tkt = t'inquiète, mdr = lol). " +
        "Keep it short and informal; keep player names, game abbreviations and emoticons unchanged.";

    private readonly HttpClient http;
    private readonly Func<Configuration> config;

    public DeepLTranslator(HttpClient http, Func<Configuration> config)
    {
        this.http = http;
        this.config = config;
    }

    public string Name => "DeepL";

    private static string BaseUrl(string key) =>
        key.Trim().EndsWith(":fx", StringComparison.Ordinal) ? "https://api-free.deepl.com" : "https://api.deepl.com";

    public async Task<EngineResult> TranslateAsync(string text, Language? source, Language target, CancellationToken ct)
    {
        var cfg = config();
        var key = cfg.DeepLApiKey.Trim();
        if (string.IsNullOrEmpty(key))
            throw new TranslationException("DeepL API key is not set (/linguabridge config).");

        var body = new Dictionary<string, object>
        {
            ["text"] = new[] { ProtectedText.HasProtected(text) ? ProtectedText.ToDeepLXml(text) : text },
            ["target_lang"] = target.Code == "EN" ? cfg.EnglishVariant : target.DeepLTarget,
            ["context"] = GameContext,
        };
        if (source != null)
            body["source_lang"] = source.DeepLSource;
        if (ProtectedText.HasProtected(text))
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
        if (!response.IsSuccessStatusCode)
        {
            var reason = (int)response.StatusCode switch
            {
                403 => "invalid DeepL API key",
                429 => "too many requests, slow down",
                456 => "monthly DeepL quota exceeded",
                _ => $"HTTP {(int)response.StatusCode}",
            };
            throw new TranslationException($"DeepL error: {reason}");
        }

        var result = await response.Content.ReadFromJsonAsync<DeepLResponse>(cancellationToken: ct).ConfigureAwait(false);
        if (result?.Translations is not { Length: > 0 } t)
            throw new TranslationException("DeepL returned an empty response.");

        var translated = ProtectedText.HasProtected(text) ? ProtectedText.FromDeepLXml(t[0].Text) : t[0].Text;
        return new EngineResult(translated, Languages.NormalizeDetected(t[0].DetectedSourceLanguage));
    }

    /// <summary>Returns (used, limit) characters for the current billing period.</summary>
    public async Task<(long Used, long Limit)> GetUsageAsync(CancellationToken ct)
    {
        var key = config().DeepLApiKey.Trim();
        if (string.IsNullOrEmpty(key))
            throw new TranslationException("DeepL API key is not set.");
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl(key)}/v2/usage");
        request.Headers.TryAddWithoutValidation("Authorization", $"DeepL-Auth-Key {key}");
        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new TranslationException($"DeepL usage error: HTTP {(int)response.StatusCode}");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        return (doc.RootElement.GetProperty("character_count").GetInt64(),
                doc.RootElement.GetProperty("character_limit").GetInt64());
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
