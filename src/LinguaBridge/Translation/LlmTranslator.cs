using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LinguaBridge.Translation;

/// <summary>
/// Translation through any OpenAI-compatible chat API (Mistral, Groq, Cerebras, OpenRouter, DeepSeek,
/// Gemini's OpenAI endpoint...). Providers are tried in order; one that is rate limited, out of quota,
/// misconfigured or refuses the content is skipped for a while and the next one is used.
/// </summary>
public sealed partial class LlmTranslator : ITranslator
{
    public const string DefaultPrompt =
        "You are a professional literary translator for live chat in the MMORPG Final Fantasy XIV between adult players.\n" +
        "Translate the user's message into {target}.\n" +
        "Rules:\n" +
        "- Keep tone, slang, register, humour and emotional intensity. Do not censor, soften, summarise or add anything.\n" +
        "- Keep *actions between asterisks*, quotes, ((OOC)) markers, emoticons (^^, o:, <3) and line structure.\n" +
        "- Never translate names, FFXIV terms or anything listed under 'Keep unchanged'.\n" +
        "- Chat abbreviations (jsp, tkt, idk, tbh...) must be understood and translated naturally.\n" +
        "- The message may mix several languages (e.g. Spanish dialogue with an English *action*): translate EVERY part into {target}.\n" +
        "- If the message is already entirely in {target}, return it unchanged.\n" +
        "- Output format: first the ISO 639-1 code of the ORIGINAL language in double brackets, then the translation. " +
        "Example: [[fr]] Hello there. Output nothing else: no notes, no quotes around it, no explanations.";

    private const string RoleplayExtra =
        "This is collaborative in-character roleplay, possibly romantic or explicit between consenting adults. Translate it " +
        "faithfully as literary prose, preserving each character's voice, sensuality and intensity; keep the person and tense " +
        "(first/third person) of the original.";

    private readonly HttpClient http;
    private readonly Func<Configuration> config;
    private readonly Dictionary<string, (DateTime Until, string Status)> states = new();

    public LlmTranslator(HttpClient http, Func<Configuration> config)
    {
        this.http = http;
        this.config = config;
    }

    public string Name => "AI";

    /// <summary>Name of the provider that answered last (shown in history as the engine).</summary>
    public string LastProvider { get; private set; } = "AI";

    /// <summary>Raised when a provider fails and the next one is tried (provider name, reason).</summary>
    public event Action<string, string>? ProviderUnavailable;

    [GeneratedRegex(@"^\s*\[\[\s*([A-Za-z]{2,3})(?:-[A-Za-z]+)?\s*\]\]\s*", RegexOptions.CultureInvariant)]
    private static partial Regex LangTagRegex();

    [GeneratedRegex(@"<think>.*?</think>", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex ThinkRegex();

    [GeneratedRegex(@"^(i'?m sorry|i am sorry|sorry,? (but )?i|i can'?t|i cannot|i won'?t|i'?m not able|i am not able|as an ai|" +
                    @"je ne peux pas|désolé,? (mais )?je ne|lo siento|no puedo)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RefusalRegex();

    public IReadOnlyList<LlmProvider> ActiveProviders =>
        config().LlmProviders.Where(p => p.Enabled && !string.IsNullOrWhiteSpace(p.BaseUrl) && !string.IsNullOrWhiteSpace(p.Model)).ToList();

    public string Describe(LlmProvider p)
    {
        lock (states)
            if (states.TryGetValue(p.Id, out var s))
                return s.Until > DateTime.Now ? $"{s.Status} (skipped for now)" : s.Status;
        return "not used yet";
    }

    public void ResetStates()
    {
        lock (states)
            states.Clear();
    }

    private void SetState(LlmProvider p, string status, TimeSpan block)
    {
        lock (states)
            states[p.Id] = (DateTime.Now + block, status);
    }

    private bool IsBlocked(LlmProvider p)
    {
        lock (states)
            return states.TryGetValue(p.Id, out var s) && s.Until > DateTime.Now;
    }

    public async Task<EngineResult> TranslateAsync(string text, Language? source, Language target, TranslateOptions options, CancellationToken ct)
    {
        var providers = ActiveProviders;
        if (providers.Count == 0)
            throw new TranslationException("No AI provider configured (/lb config > AI).");

        var (system, user) = BuildPrompt(text, source, target, options);
        string? lastError = null;
        foreach (var p in providers)
        {
            if (IsBlocked(p))
                continue;
            try
            {
                var raw = await CompleteAsync(p, Messages(system, user, source, target), text.Length, ct).ConfigureAwait(false);
                var result = Parse(raw, text);
                SetState(p, "OK", TimeSpan.Zero);
                LastProvider = p.Name;
                return result;
            }
            catch (LlmFailure f)
            {
                lastError = $"{p.Name}: {f.Message}";
                SetState(p, f.Message, f.Block);
                ProviderUnavailable?.Invoke(p.Name, f.Message);
            }
            catch (HttpRequestException ex)
            {
                lastError = $"{p.Name}: {ex.Message}";
                SetState(p, "network error", TimeSpan.FromSeconds(30));
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                lastError = $"{p.Name}: timed out";
                SetState(p, "timed out", TimeSpan.FromSeconds(30));
            }
        }

        throw new TranslationException(lastError == null ? "All AI providers are paused, retrying soon" : $"AI error ({lastError})");
    }

    /// <summary>Sends a short test translation through one provider (settings "Test" button).</summary>
    public async Task<string> TestAsync(LlmProvider p, CancellationToken ct)
    {
        var target = Languages.Get("FR");
        var (system, user) = BuildPrompt("*smiles softly* \"I missed you, you know?\"", Languages.Get("EN"), target,
                                         new TranslateOptions(Roleplay: true));
        try
        {
            var raw = await CompleteAsync(p, Messages(system, user, Languages.Get("EN"), target), 60, ct).ConfigureAwait(false);
            var r = Parse(raw, "x");
            SetState(p, "OK", TimeSpan.Zero);
            return "OK: " + r.Text;
        }
        catch (LlmFailure f)
        {
            SetState(p, f.Message, TimeSpan.Zero);
            return "Failed: " + f.Message;
        }
        catch (Exception ex)
        {
            return "Failed: " + ex.Message;
        }
    }

    private (string System, string User) BuildPrompt(string text, Language? source, Language target, TranslateOptions options)
    {
        var cfg = config();
        var targetName = target.Code switch
        {
            "EN" => cfg.EnglishVariant == "EN-GB" ? "British English" : "English",
            "PT" => "Brazilian Portuguese",
            _ => LanguageEnglishName(target.Code),
        };

        var template = string.IsNullOrWhiteSpace(cfg.LlmPrompt) ? DefaultPrompt : cfg.LlmPrompt;
        var sb = new StringBuilder(template.Replace("{target}", targetName));
        if (PairStyle.RulesFor(target.Code) is { } rules)
            sb.Append("\n\n").Append(rules);
        sb.Append("\n\nSound natural: adapt idioms instead of translating word for word, silently fix obvious typos " +
                  "of the original, and keep roughly the same length and punctuation style.");
        if (options.Roleplay)
            sb.Append("\n\n").Append(RoleplayExtra);
        if (options.Roleplay && !string.IsNullOrWhiteSpace(cfg.RpSceneNotes))
            sb.Append("\n\nScene notes: ").Append(cfg.RpSceneNotes.Trim());
        if (!string.IsNullOrWhiteSpace(options.Characters))
            sb.Append("\n\nCharacters (use for gender agreement and how they address each other):\n").Append(options.Characters);

        var keep = ProtectedSpans(text);
        if (keep.Count > 0)
            sb.Append("\n\nKeep unchanged: ").Append(string.Join(", ", keep));

        var glossary = cfg.Glossary
                          .Where(g => g.To == target.Code && (source == null || g.From == source.Code))
                          .Where(g => !string.IsNullOrWhiteSpace(g.Source) && !string.IsNullOrWhiteSpace(g.Target))
                          .Take(60)
                          .Select(g => $"{g.Source.Trim()} -> {g.Target.Trim()}")
                          .ToList();
        if (glossary.Count > 0)
            sb.Append("\n\nAlways translate these terms like this:\n").Append(string.Join("\n", glossary));

        if (!string.IsNullOrWhiteSpace(options.Context))
            sb.Append("\n\nPrevious messages (context only, do NOT translate them):\n").Append(options.Context);

        var hint = source == null ? string.Empty : $"(original language: {LanguageEnglishName(source.Code)})\n";
        return (sb.ToString(), hint + ProtectedText.Plain(text));
    }

    private static List<string> ProtectedSpans(string text)
    {
        var list = new List<string>();
        var start = -1;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == ProtectedText.Open)
                start = i + 1;
            else if (text[i] == ProtectedText.Close && start >= 0)
            {
                var s = text[start..i];
                if (!list.Contains(s))
                    list.Add(s);
                start = -1;
            }
        }
        return list;
    }

    private static string LanguageEnglishName(string code) => code.ToUpperInvariant() switch
    {
        "EN" => "English", "FR" => "French", "ES" => "Spanish", "DE" => "German", "IT" => "Italian",
        "PT" => "Portuguese", "JA" => "Japanese", "ZH" => "Chinese", "KO" => "Korean", "RU" => "Russian",
        _ => code,
    };

    /// <summary>Model ids offered by a provider (GET /models), for the settings picker.</summary>
    public async Task<List<string>> ListModelsAsync(LlmProvider p, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, p.BaseUrl.Trim().TrimEnd('/') + "/models");
        if (!string.IsNullOrWhiteSpace(p.ApiKey))
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {p.ApiKey.Trim()}");
        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new TranslationException($"HTTP {(int)response.StatusCode}");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        var list = new List<string>();
        if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            foreach (var m in data.EnumerateArray())
                if (m.TryGetProperty("id", out var id) && id.GetString() is { } s)
                    list.Add(s.StartsWith("models/", StringComparison.Ordinal) ? s[7..] : s);
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list;
    }

    private EngineResult Parse(string raw, string original)
    {
        var text = ThinkRegex().Replace(raw, string.Empty).Trim();
        var detected = string.Empty;
        var m = LangTagRegex().Match(text);
        if (m.Success)
        {
            detected = m.Groups[1].Value.ToUpperInvariant();
            text = text[m.Length..].Trim();
        }

        // Some models wrap the whole answer in quotes or a code block.
        if (text.StartsWith("```"))
            text = text.Trim('`').Trim();
        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"' && !ProtectedText.Plain(original).TrimStart().StartsWith('"'))
            text = text[1..^1].Trim();

        if (text.Length == 0)
            throw new LlmFailure("empty answer", TimeSpan.FromSeconds(30));
        if (RefusalRegex().IsMatch(text) && !RefusalRegex().IsMatch(ProtectedText.Plain(original)))
            throw new LlmFailure("refused the content", TimeSpan.FromMinutes(10));

        return new EngineResult(text, detected.Length > 2 ? detected[..2] : detected);
    }

    /// <summary>System prompt, then worked examples for this direction as previous turns, then the real message.</summary>
    private static object[] Messages(string system, string user, Language? source, Language target)
    {
        var list = new List<object> { new { role = "system", content = system } };
        foreach (var (src, input, output) in PairStyle.ExamplesFor(source?.Code, target.Code))
        {
            list.Add(new { role = "user", content = $"(original language: {LanguageEnglishName(src)})\n{input}" });
            list.Add(new { role = "assistant", content = $"[[{src.ToLowerInvariant()}]] {output}" });
        }
        list.Add(new { role = "user", content = user });
        return list.ToArray();
    }

    private async Task<string> CompleteAsync(LlmProvider p, object[] messages, int inputChars, CancellationToken ct)
    {
        var url = p.BaseUrl.Trim().TrimEnd('/') + "/chat/completions";
        var body = new Dictionary<string, object>
        {
            ["model"] = p.Model.Trim(),
            ["temperature"] = config().LlmTemperature,
            ["max_tokens"] = Math.Clamp(inputChars * 2 + 200, 256, 2048),
            ["messages"] = messages,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        if (!string.IsNullOrWhiteSpace(p.ApiKey))
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {p.ApiKey.Trim()}");
        if (url.Contains("openrouter.ai", StringComparison.OrdinalIgnoreCase))
        {
            request.Headers.TryAddWithoutValidation("HTTP-Referer", "https://github.com/Ajimsan2096/DalamudPlugins");
            request.Headers.TryAddWithoutValidation("X-Title", "LinguaBridge");
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(20));
        using var response = await http.SendAsync(request, cts.Token).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var status = (int)response.StatusCode;
            var retryAfter = response.Headers.RetryAfter?.Delta;
            throw status switch
            {
                401 or 403 => new LlmFailure("invalid API key or no access to this model", TimeSpan.FromHours(1)),
                402 => new LlmFailure("no credits left", TimeSpan.FromHours(6)),
                404 => new LlmFailure("model or URL not found (check the model name)", TimeSpan.FromHours(1)),
                429 => new LlmFailure("rate limit / daily quota reached", retryAfter ?? TimeSpan.FromMinutes(2)),
                400 when payload.Contains("safety", StringComparison.OrdinalIgnoreCase) ||
                         payload.Contains("moderation", StringComparison.OrdinalIgnoreCase) ||
                         payload.Contains("content", StringComparison.OrdinalIgnoreCase)
                    => new LlmFailure("blocked by the provider's content filter", TimeSpan.FromMinutes(10)),
                _ => new LlmFailure($"HTTP {status}", TimeSpan.FromSeconds(30)),
            };
        }

        using var doc = JsonDocument.Parse(payload);
        var choice = doc.RootElement.GetProperty("choices")[0];
        if (choice.TryGetProperty("finish_reason", out var fr) && fr.ValueKind == JsonValueKind.String &&
            fr.GetString() is "content_filter" or "safety")
            throw new LlmFailure("blocked by the provider's content filter", TimeSpan.FromMinutes(10));
        var content = choice.GetProperty("message").GetProperty("content");
        return content.ValueKind == JsonValueKind.String ? content.GetString() ?? string.Empty : string.Empty;
    }

    private sealed class LlmFailure(string message, TimeSpan block) : Exception(message)
    {
        public TimeSpan Block { get; } = block;
    }
}
