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
        ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "g", ['д'] = "d", ['е'] = "e", ['ё'] = "e", ['ж'] = "zh",
        ['з'] = "z", ['и'] = "i", ['й'] = "y", ['к'] = "k", ['л'] = "l", ['м'] = "m", ['н'] = "n", ['о'] = "o",
        ['п'] = "p", ['р'] = "r", ['с'] = "s", ['т'] = "t", ['у'] = "u", ['ф'] = "f", ['х'] = "h", ['ц'] = "ts",
        ['ч'] = "ch", ['ш'] = "sh", ['щ'] = "sch", ['ъ'] = "", ['ы'] = "y", ['ь'] = "", ['э'] = "e", ['ю'] = "yu",
        ['я'] = "ya",
        // Ukrainian / Belarusian letters that also show up
        ['і'] = "i", ['ї'] = "yi", ['є'] = "ye", ['ґ'] = "g", ['ў'] = "u",
    };

    /// <summary>Latin sequences to Cyrillic, longest first.</summary>
    private static readonly (string Latin, string Cyrillic)[] ToCyrillicMap =
    {
        ("shch", "щ"), ("sch", "щ"), ("zh", "ж"), ("kh", "х"), ("ch", "ч"), ("sh", "ш"), ("ts", "ц"),
        ("yo", "ё"), ("jo", "ё"), ("yu", "ю"), ("ju", "ю"), ("ya", "я"), ("ja", "я"),
        ("a", "а"), ("b", "б"), ("v", "в"), ("w", "ш"), ("g", "г"), ("d", "д"), ("e", "е"), ("z", "з"), ("i", "и"),
        ("j", "й"), ("k", "к"), ("q", "к"), ("l", "л"), ("m", "м"), ("n", "н"), ("o", "о"), ("p", "п"), ("r", "р"),
        ("s", "с"), ("t", "т"), ("u", "у"), ("f", "ф"), ("h", "х"), ("c", "ц"), ("x", "х"), ("'", "ь"), ("4", "ч"),
    };

    private const string LatinVowels = "aeiouy";

    /// <summary>
    /// Frequent words that letter-by-letter conversion gets wrong in chat translit: a dropped soft sign
    /// (tolko = только), е/э/ё, irregular spellings (shas, evo, ischo). Keys are lower-case translit.
    /// </summary>
    private static readonly Dictionary<string, string> Lexicon = BuildLexicon(new (string Variants, string Word)[]
    {
        ("tolko tolka tok", "только"), ("ochen ochin och", "очень"), ("vsyo vso vsio", "всё"),
        ("eshe esche eshche eshchyo eshyo eschyo ischo isho esho yeshche yeshyo", "ещё"),
        ("seychas sejchas sechas sichas seichas shas schas shchas sha scha", "сейчас"),
        ("est yest", "есть"), ("esli yesli", "если"), ("ego evo yevo yego", "его"), ("eyo yeyo eio", "её"),
        ("ee", "её"), ("emu yemu", "ему"), ("eti", "эти"), ("eto eta", "это"), ("etot", "этот"), ("etu", "эту"),
        ("etom", "этом"), ("etoy etoj", "этой"), ("etogo etovo", "этого"), ("etomu", "этому"), ("etih etikh", "этих"),
        ("ekh eh ehh", "эх"), ("ey ei", "эй"),
        ("den", "день"), ("dengi", "деньги"), ("zhizn", "жизнь"), ("lyubov ljubov lubov", "любовь"),
        ("noch", "ночь"), ("doch", "дочь"), ("mat", "мать"), ("tetrad", "тетрадь"), ("dver", "дверь"),
        ("voobshe voobshche voobsche vobshe vapshe vabshe vaabshe", "вообще"),
        ("obyazatelno objazatelno", "обязательно"), ("deystvitelno dejstvitelno", "действительно"),
        ("normalno normalna", "нормально"), ("realno", "реально"), ("tochno", "точно"), ("pravda", "правда"),
        ("pyat", "пять"), ("shest", "шесть"), ("sem", "семь"), ("vosem", "восемь"), ("devyat devjat", "девять"),
        ("desyat desjat", "десять"), ("bolshoy bolshoj bolshoi", "большой"), ("bolshe", "больше"),
        ("malenkiy malenkij malenkii", "маленький"), ("menshe", "меньше"), ("pust", "пусть"), ("bud", "будь"),
        ("vsegda", "всегда"), ("nikogda", "никогда"), ("chtoto chto-to shtoto", "что-то"),
        ("kakoyto kakoj-to kakoy-to", "какой-то"), ("kogda-nibud kogdanibud", "когда-нибудь"),
        ("izvini izvinite", "извини"), ("sorri sori sorian soryan", "извини"),
        ("spokoynoy spokojnoj spokoinoi", "спокойной"), ("spokoyno spokojno", "спокойно"),
        ("privet privetik", "привет"), ("poka pokeda", "пока"), ("spasibo spasib spasiba pasib pasiba", "спасибо"),
        ("pozhaluysta pozhalujsta pozhalusta pojaluista pojalusta pzhlst", "пожалуйста"),
        ("horosho khorosho harasho xorosho xorowo horoso", "хорошо"), ("ladno lan", "ладно"),
        ("konechno konesh koneshno", "конечно"), ("davay davai davaj", "давай"), ("davayte davaite", "давайте"),
        ("chto shto 4to", "что"), ("chtoby shtoby 4toby", "чтобы"), ("chego chevo shevo 4ego", "чего"),
        ("che 4e cho", "чё"), ("nichego nichevo nicho", "ничего"), ("kto", "кто"), ("tebya tebja", "тебя"),
        ("menya menja", "меня"), ("tozhe toje", "тоже"), ("uzhe uje", "уже"), ("blin", "блин"),
        ("kruto kryto", "круто"), ("prikolno", "прикольно"), ("klass klassno", "классно"), ("kak", "как"),
        ("igra", "игра"), ("igrat", "играть"), ("poigrat", "поиграть"), ("sygrat", "сыграть"),
        ("lyublyu ljublju lublu", "люблю"), ("skuchayu skuchaju", "скучаю"), ("obnimayu obnimaju", "обнимаю"),
        ("celuyu tseluyu celuju", "целую"), ("milaya", "милая"), ("miliy milyy milyi", "милый"),
        ("solnyshko solnishko", "солнышко"), ("zaya zaika zayka", "зайка"), ("kotik", "котик"),
        ("rebyata rebjata rebyat", "ребята"), ("drug", "друг"), ("podruga", "подруга"),
        ("segodnya segodnja sevodnya sevodnja", "сегодня"), ("zavtra", "завтра"), ("vchera", "вчера"),
        ("dobroe", "доброе"), ("dobryy dobryi dobrij dobriy", "добрый"), ("utro utra", "утро"), ("vecher", "вечер"),
        ("nochi", "ночи"), ("dela", "дела"), ("zdravstvuy zdravstvuyte zdrastvuy zdrasti zdraste", "здравствуй"),
        ("vmeste", "вместе"), ("potom", "потом"), ("sovsem", "совсем"), ("prosto", "просто"), ("ochered", "очередь"),
        ("pochemu", "почему"), ("potomu", "потому"), ("zachem", "зачем"), ("gde", "где"), ("kuda", "куда"),
        ("rus russkiy russkij russkii", "русский"), ("angliyskiy angliyskij angliiskii", "английский"),
        ("ispanskiy ispanskij", "испанский"), ("nemetskiy nemeckij nemetskij", "немецкий"),
        ("vy", "вы"), ("my", "мы"), ("ty", "ты"), ("ya ja", "я"), ("da", "да"), ("net", "нет"), ("ne", "не"),
        ("nu", "ну"), ("i", "и"), ("v", "в"), ("s", "с"), ("k", "к"), ("u", "у"), ("o", "о"), ("a", "а"),
    });

    /// <summary>Words that make an infinitive follow (mogu igrat -> могу играть).</summary>
    private static readonly HashSet<string> InfinitiveTriggers = new(StringComparer.Ordinal)
    {
        "mogu", "mozhesh", "mozhet", "mozhem", "mozhete", "mogut", "mog", "mogla", "mogli", "hochu", "khochu",
        "hochesh", "khochesh", "hochet", "khochet", "hotim", "khotim", "hotite", "khotite", "hotyat", "khotyat",
        "hotel", "khotel", "hotela", "khotela", "nado", "nuzhno", "mozhno", "nelzya", "budu", "budesh", "budet",
        "budem", "budete", "budut", "lyublyu", "ljublju", "lublu", "lyubish", "lyubit", "umeyu", "umeesh", "umeet",
        "nachal", "nachala", "nachali", "stal", "stala", "stali", "pora", "perestan", "perestal", "perestala",
        "poprobuy", "poprobuyu", "davay", "davai", "davaj", "davayte", "reshil", "reshila", "sobirayus", "gotov",
        "gotova", "chtoby", "shtoby", "nravitsya", "nravitsa", "zabyl", "zabyla", "uchus", "nauchis", "len",
    };

    /// <summary>Nouns ending in -at that are not infinitives (brat is брат, not брать).</summary>
    private static readonly HashSet<string> AtNouns = new(StringComparer.Ordinal)
    {
        "brat", "soldat", "salat", "rezultat", "format", "avtomat", "halat", "khalat", "zakat", "chat", "kanat",
        "plakat", "kandidat", "shtat", "deputat", "aromat", "agregat", "diplomat", "adresat", "aristokrat",
        "kombat", "akrobat", "magnat", "palat", "zarplat", "granat", "mat", "bat", "kat", "sat", "vat", "pat",
        "start", "kvadrat", "apparat", "sportzal", "attestat", "tsitat", "citat", "kontrakt", "beat",
    };

    /// <summary>Words that start with э (otherwise a word-initial e is е: esli, est, ego).</summary>
    private static readonly HashSet<string> EStartWords = new(StringComparer.Ordinal)
    {
        "ekran", "era", "effekt", "effektivno", "eksperiment", "ekzamen", "energiya", "elf", "elfiyka", "etazh",
        "epizod", "emotsii", "emociya", "emotsiya", "event", "ekipirovka", "elitka", "ekspert", "ekskursiya",
        "elektrichestvo", "etap", "eho", "ekho", "eti", "etu", "etoy", "etih", "etim", "etimi",
    };

    /// <summary>A frequent translit word from the list above (3+ letters, so "da", "ty" or "ya" do not count).</summary>
    public static bool IsLexiconWord(string lowerWord) => lowerWord.Length >= 3 && Lexicon.ContainsKey(lowerWord);

    private static Dictionary<string, string> BuildLexicon((string Variants, string Word)[] entries)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (variants, word) in entries)
            foreach (var v in variants.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                d.TryAdd(v, word);
        return d;
    }

    [GeneratedRegex(@"[A-Za-z'4]+", RegexOptions.CultureInvariant)]
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
    /// Cyrillic is left alone; MMO shorthand (gg, dps, tank...) is kept as is. Chat translit drops the
    /// soft sign, so a word list and a few grammar rules put it back where it is almost certain
    /// (tolko -> только, mogu igrat -> могу играть, budesh -> будешь, nravitsya -> нравится).
    /// </summary>
    public static string ToCyrillic(string text)
    {
        if (HasCyrillic(text))
            return text;
        var sb = new StringBuilder(text.Length);
        var last = 0;
        var infinitiveNext = false;
        foreach (Match m in LatinWordRegex().Matches(text))
        {
            var gap = text[last..m.Index];
            sb.Append(gap);
            last = m.Index + m.Length;
            if (gap.IndexOfAny(new[] { ',', '.', '!', '?', ';', ':', '(', ')' }) >= 0)
                infinitiveNext = false; // "on ne mozhet, brat moy..." : brat is not an infinitive here

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
            var key = core.Trim('\'').ToLowerInvariant();
            if (!key.Any(char.IsLetter) || (LanguageDetector.IsNeutralWord(key) && !Lexicon.ContainsKey(key)))
            {
                sb.Append(word);
                infinitiveNext = false;
                continue;
            }

            sb.Append(lead).Append(ConvertWord(core, infinitiveNext)).Append(trail);
            // "ne" / "zhe" between the trigger and the verb keep the expectation (ne mogu ne dumat).
            if (key is not ("ne" or "zhe" or "uzhe" or "tozhe" or "eshe" or "esche" or "bolshe" or "prosto"))
                infinitiveNext = InfinitiveTriggers.Contains(key);
        }
        sb.Append(text, last, text.Length - last);
        return sb.ToString();
    }

    private static string ConvertWord(string word, bool infinitiveExpected)
    {
        var lower = word.ToLowerInvariant();
        string cyr;
        if (Lexicon.TryGetValue(lower, out var known) && !(infinitiveExpected && lower == "mat"))
        {
            cyr = known;
        }
        else
        {
            cyr = Letters(lower);
            cyr = FixEnding(lower, cyr, infinitiveExpected);
        }
        return ApplyCase(word, cyr);
    }

    /// <summary>Letter-by-letter conversion of a lower-case translit word.</summary>
    private static string Letters(string lower)
    {
        var sb = new StringBuilder(lower.Length);
        var i = 0;
        while (i < lower.Length)
        {
            var prev = i > 0 ? lower[i - 1] : '\0';
            var next = i + 1 < lower.Length ? lower[i + 1] : '\0';
            string? cyr = null;
            var len = 1;

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
            else if (lower[i] == 'e' && i == 0)
            {
                cyr = EStartWords.Contains(lower) ? "э" : "е";
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

            sb.Append(cyr ?? lower[i].ToString());
            i += len;
        }
        return sb.ToString();
    }

    /// <summary>Endings where chat translit loses the soft sign or spells -тся as "tsya".</summary>
    private static string FixEnding(string lower, string cyr, bool infinitiveExpected)
    {
        if (lower.Length < 3 || cyr.EndsWith('ь'))
            return cyr;

        // Reflexive verbs: nravitsya -> нравится, after "mogu/hochu..." -> учиться.
        if (lower.EndsWith("tsya") || lower.EndsWith("tsa") && infinitiveExpected)
            return cyr[..^2] + (infinitiveExpected ? "ться" : "тся");

        // Infinitive after a trigger: mogu igrat -> могу играть, hochu spat -> хочу спать.
        if (infinitiveExpected && lower.Length >= 3 && lower[^1] == 't' && LatinVowels.Contains(lower[^2]))
            return cyr + "ь";
        if (infinitiveExpected && lower.EndsWith("ch") && lower.Length >= 4)
            return cyr + "ь"; // pomoch -> помочь

        // -at at the end is nearly always an infinitive (delat, igrat, rabotat), except a few nouns
        // and 3rd-person plurals after ж/ш/ч/щ (lezhat, slyshat).
        if (lower.Length >= 5 && lower.EndsWith("at") && !AtNouns.Contains(lower) &&
            !lower.EndsWith("zhat") && !lower.EndsWith("shat") && !lower.EndsWith("chat") && !lower.EndsWith("hat"))
            return cyr + "ь";

        // 2nd person singular always ends in -шь: budesh -> будешь, hochesh -> хочешь, vidish -> видишь.
        if (lower.Length >= 4 && (lower.EndsWith("esh") || lower.EndsWith("ish") || lower.EndsWith("yosh")) &&
            lower is not ("malysh" or "kamysh"))
            return cyr + "ь";

        // Reflexive past and 1st person: spalos -> спалось, poluchilos -> получилось, boyus -> боюсь.
        if (lower.Length >= 5 && (lower.EndsWith("los") || lower.EndsWith("las") || lower.EndsWith("lis") ||
                                  lower.EndsWith("yus") || lower.EndsWith("chus")))
            return cyr + "ь";

        // russkii -> русский
        if (lower.Length >= 4 && lower.EndsWith("ii"))
            return cyr[..^1] + "й";
        return cyr;
    }

    private static string ApplyCase(string word, string cyr)
    {
        var letters = word.Where(char.IsLetter).ToList();
        if (letters.Count > 1 && letters.All(char.IsUpper))
            return cyr.ToUpperInvariant();
        return letters.Count > 0 && char.IsUpper(letters[0]) && cyr.Length > 0
            ? char.ToUpperInvariant(cyr[0]) + cyr[1..]
            : cyr;
    }
}
