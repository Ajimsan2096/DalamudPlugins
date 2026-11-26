using System;
using System.Collections.Generic;
using System.Linq;

namespace LinguaBridge.Translation;

/// <summary>A language supported by the plugin, with the codes each engine expects.</summary>
public sealed record Language(
    string Code,          // short code shown in chat, e.g. "EN"
    string Name,          // English name; shown through L() in the interface language
    string DeepLSource,   // DeepL source_lang
    string DeepLTarget,   // DeepL target_lang
    string GoogleCode);   // Google Translate code

public static class Languages
{
    public static readonly IReadOnlyList<Language> All = new[]
    {
        new Language("EN", "English",    "EN", "EN-US", "en"),
        new Language("FR", "French",     "FR", "FR",    "fr"),
        new Language("ES", "Spanish",    "ES", "ES",    "es"),
        new Language("DE", "German",     "DE", "DE",    "de"),
        new Language("IT", "Italian",    "IT", "IT",    "it"),
        new Language("PT", "Portuguese", "PT", "PT-BR", "pt"),
        new Language("NL", "Dutch",      "NL", "NL",    "nl"),
        new Language("JA", "Japanese",   "JA", "JA",    "ja"),
        // FFXIV's chat font has no Cyrillic: Russian is written in Latin letters in game ("privet").
        new Language("RU", "Russian",    "RU", "RU",    "ru"),
    };

    public static Language Get(string code) =>
        All.FirstOrDefault(l => l.Code.Equals(code, StringComparison.OrdinalIgnoreCase)) ?? All[0];

    public static Language? TryGet(string code) =>
        All.FirstOrDefault(l => l.Code.Equals(code, StringComparison.OrdinalIgnoreCase));

    /// <summary>Maps an engine's detected-language code ("en", "EN", "en-US", "zh-CN"...) to our short code.</summary>
    public static string NormalizeDetected(string? detected)
    {
        if (string.IsNullOrWhiteSpace(detected))
            return string.Empty;
        var dash = detected.IndexOf('-');
        var core = dash > 0 ? detected[..dash] : detected;
        return core.ToUpperInvariant();
    }
}
