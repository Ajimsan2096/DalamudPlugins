using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace LinguaBridge.Translation;

/// <summary>
/// Russian in a game whose chat font has no Cyrillic: Russian players write "translit"
/// ("privet, kak dela?"). Outgoing Russian is written that way, and incoming translit is turned
/// back into Cyrillic so DeepL / Google can actually read it.
/// </summary>
public static partial class Translit
{
    private static readonly Dictionary<char, string> ToLatinMap = new()
    {
        ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "g", ['д'] = "d", ['е'] = "e", ['ё'] = "yo", ['ж'] = "zh",
        ['з'] = "z", ['и'] = "i", ['й'] = "y", ['к'] = "k", ['л'] = "l", ['м'] = "m", ['н'] = "n", ['о'] = "o",
        ['п'] = "p", ['р'] = "r", ['с'] = "s", ['т'] = "t", ['у'] = "u", ['ф'] = "f", ['х'] = "h", ['ц'] = "ts",
        ['ч'] = "ch", ['ш'] = "sh", ['щ'] = "sch", ['ъ'] = "", ['ы'] = "y", ['ь'] = "'", ['э'] = "e", ['ю'] = "yu",
        ['я'] = "ya",
        // Ukrainian / Belarusian letters that also show up
        ['і'] = "i", ['ї'] = "yi", ['є'] = "ye", ['ґ'] = "g", ['ў'] = "u",
    };

    /// <summary>Latin sequences to Cyrillic, longest first.</summary>
    private static readonly (string Latin, string Cyrillic)[] ToCyrillicMap =
    {
        ("shch", "щ"), ("sch", "щ"), ("zh", "ж"), ("kh", "х"), ("ch", "ч"), ("sh", "ш"), ("ts", "ц"),
        ("yo", "ё"), ("jo", "ё"), ("yu", "ю"), ("ju", "ю"), ("ya", "я"), ("ja", "я"),
        ("a", "а"), ("b", "б"), ("v", "в"), ("w", "в"), ("g", "г"), ("d", "д"), ("e", "е"), ("z", "з"), ("i", "и"),
        ("j", "й"), ("k", "к"), ("q", "к"), ("l", "л"), ("m", "м"), ("n", "н"), ("o", "о"), ("p", "п"), ("r", "р"),
        ("s", "с"), ("t", "т"), ("u", "у"), ("f", "ф"), ("h", "х"), ("c", "ц"), ("x", "кс"), ("'", "ь"),
    };

    private const string LatinVowels = "aeiouy";

    [GeneratedRegex(@"[A-Za-z']+", RegexOptions.CultureInvariant)]
    private static partial Regex LatinWordRegex();

    public static bool IsCyrillic(char c) => c is >= 'Ѐ' and <= 'ӿ';

    public static bool HasCyrillic(string text) => text.Any(IsCyrillic);

    /// <summary>Cyrillic to Latin letters the game can show ("Привет, как дела?" -> "Privet, kak dela?").</summary>
    public static string ToLatin(string text)
    {
        if (!HasCyrillic(text))
            return text;
        var sb = new StringBuilder(text.Length + 8);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            var lower = char.ToLowerInvariant(c);
            if (!IsCyrillic(c) || !ToLatinMap.TryGetValue(lower, out var latin))
            {
                sb.Append(c);
                continue;
            }
            if (latin.Length == 0 || c == lower)
            {
                sb.Append(latin);
                continue;
            }
            // Capital: "ЖДУ" -> "ZHDU", but "Женя" -> "Zhenya".
            var nextUpper = i + 1 < text.Length && char.IsUpper(text[i + 1]);
            var prevUpper = i > 0 && char.IsUpper(text[i - 1]);
            sb.Append(nextUpper || prevUpper ? latin.ToUpperInvariant() : char.ToUpperInvariant(latin[0]) + latin[1..]);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Translit back to Cyrillic ("privet, kak dela?" -> "привет, как дела?"). Text that already has
    /// Cyrillic is left alone; MMO shorthand (gg, dps, tank...) is kept as is.
    /// </summary>
    public static string ToCyrillic(string text)
    {
        if (HasCyrillic(text))
            return text;
        return LatinWordRegex().Replace(text, m =>
        {
            // Quotes around a word ('privet') stay quotes; an apostrophe inside or at the end ("nol'") is ь.
            var word = m.Value;
            var core = word.TrimStart('\'');
            var lead = word[..(word.Length - core.Length)];
            var trail = string.Empty;
            if (lead.Length > 0 && core.EndsWith('\''))
            {
                core = core[..^1];
                trail = "'";
            }
            if (core.Trim('\'').Length == 0 || LanguageDetector.IsNeutralWord(core.Trim('\'')))
                return word;
            return lead + ConvertWord(core) + trail;
        });
    }

    private static string ConvertWord(string word)
    {
        var lower = word.ToLowerInvariant();
        var sb = new StringBuilder(word.Length);
        var i = 0;
        while (i < lower.Length)
        {
            var prev = i > 0 ? lower[i - 1] : '\0';
            string? cyr = null;
            var len = 1;

            var next = i + 1 < lower.Length ? lower[i + 1] : '\0';
            if (lower[i] == 'y' && next == 'e')
            {
                cyr = "е"; // "yeshche" -> "ещё"
                len = 2;
            }
            else if (lower[i] == 'y' && next is not ('a' or 'u' or 'o'))
            {
                // After a vowel (or at the start) it is й ("moy", "zdravstvuy"), after a consonant ы ("ty", "my").
                // "ya", "yu", "yo" fall through to the table (я, ю, ё).
                cyr = prev == '\0' || LatinVowels.Contains(prev) ? "й" : "ы";
            }
            else if (lower[i] == 'e' && (prev == '\0' || prev == '\''))
            {
                cyr = "э"; // "eto" -> "это"
            }
            else
            {
                foreach (var (latin, c) in ToCyrillicMap)
                {
                    if (string.CompareOrdinal(lower, i, latin, 0, latin.Length) == 0)
                    {
                        cyr = c;
                        len = latin.Length;
                        break;
                    }
                }
            }

            cyr ??= lower[i].ToString();
            var upper = char.IsUpper(word[i]);
            sb.Append(upper ? cyr.ToUpperInvariant() : cyr);
            i += len;
        }

        return sb.ToString();
    }
}
