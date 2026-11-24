using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace LinguaBridge.Localization;

/// <summary>
/// Interface translations. The English text is the key (<c>L("Settings")</c>); each language is an embedded
/// <c>Localization/xx.json</c> mapping English to the translation. Anything missing falls back to English,
/// so a new string never shows up blank.
/// </summary>
public static class Loc
{
    /// <summary>Interface languages: the four of the FFXIV global client, each named in its own language.</summary>
    public static readonly (string Code, string Native)[] UiLanguages =
    {
        ("en", "English"), ("ja", "日本語"), ("de", "Deutsch"), ("fr", "Français"),
    };

    private static volatile Dictionary<string, string> table = new();

    public static string Current { get; private set; } = "en";

    public static bool IsSupported(string? code) => code != null && UiLanguages.Any(l => l.Code == code);

    /// <summary>Best guess for a first run: Dalamud's own UI language if we have it, else English.</summary>
    public static string Detect(string? dalamudLanguage)
    {
        var code = dalamudLanguage?.Trim().ToLowerInvariant();
        if (code is { Length: > 2 })
            code = code[..2];
        return IsSupported(code) ? code! : "en";
    }

    public static void SetLanguage(string? code)
    {
        code = IsSupported(code) ? code! : "en";
        Current = code;
        if (code == "en")
        {
            table = new Dictionary<string, string>();
            return;
        }
        try
        {
            var asm = typeof(Loc).Assembly;
            using var stream = asm.GetManifestResourceStream($"LinguaBridge.Localization.{code}.json");
            if (stream == null)
            {
                table = new Dictionary<string, string>();
                return;
            }
            using var doc = JsonDocument.Parse(stream);
            var loaded = new Dictionary<string, string>();
            foreach (var entry in doc.RootElement.EnumerateObject())
                loaded[entry.Name] = entry.Value.GetString() ?? string.Empty;
            table = loaded;
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, $"Could not load the {code} interface translation; using English.");
            table = new Dictionary<string, string>();
        }
    }

    /// <summary>Translates an interface string (English in, current language out).</summary>
    public static string L(string english) =>
        table.TryGetValue(english, out var t) && !string.IsNullOrEmpty(t) ? t : english;

    /// <summary>Translates a format string, then fills in {0}, {1}...</summary>
    public static string LF(string english, params object?[] args)
    {
        try
        {
            return string.Format(L(english), args);
        }
        catch (FormatException)
        {
            return string.Format(english, args);
        }
    }
}
