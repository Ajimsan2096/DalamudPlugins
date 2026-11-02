using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LinguaBridge.Translation;

/// <summary>
/// Google Translate's public web endpoint. No API key needed, but it is unofficial and may
/// rate-limit heavy use. Good as a zero-setup fallback; DeepL gives better EN/FR quality.
/// </summary>
public sealed class GoogleTranslator(HttpClient http) : ITranslator
{
    public string Name => "Google";

    public async Task<EngineResult> TranslateAsync(string text, Language? source, Language target, TranslateOptions options, CancellationToken ct)
    {
        var url = "https://translate.googleapis.com/translate_a/single?client=gtx&dt=t" +
                  $"&sl={source?.GoogleCode ?? "auto"}&tl={target.GoogleCode}&q={Uri.EscapeDataString(ProtectedText.Plain(text))}";
        _ = options; // Google has no context/glossary support

        using var response = await http.GetAsync(url, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new TranslationException((int)response.StatusCode == 429
                ? "Google error: rate limited, try again later or switch to DeepL"
                : $"Google error: HTTP {(int)response.StatusCode}");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
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
            throw new TranslationException("Google returned an empty response.");

        return new EngineResult(sb.ToString(), Languages.NormalizeDetected(detected));
    }
}
