using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace LinguaBridge.Translation;

/// <summary>
/// Very small, offline language guesser used to avoid paying for an extra API call.
/// It only has to choose between the two configured languages; when unsure it returns null
/// and the translation engine's own detection is used instead.
/// </summary>
public static partial class LanguageDetector
{
    private static readonly Dictionary<string, HashSet<string>> StopWords = new()
    {
        ["EN"] = Set("the a an and or but is are was were be been am i you he she it we they me my your our their this that these those " +
                     "to of in on at for with from by about as not no yes do does did have has had will would can could should " +
                     "what who where when why how there here just so if then than too very all any some get got go going im i'm " +
                     "you're it's don't dont can't cant thanks thank please sorry want need know think let lets let's up out"),
        ["FR"] = Set("le la les un une des et ou mais est sont était être suis es sommes êtes je tu il elle on nous vous ils elles " +
                     "me te se mon ma mes ton ta tes son sa ses notre votre leur ce cet cette ces de du au aux en dans sur pour avec " +
                     "par pas ne non oui qui que quoi où quand pourquoi comment très tout tous bien merci salut bonjour bonsoir " +
                     "c'est j'ai t'es y a ça ca aussi plus moins peux veux faut fait vais va allez désolé d'accord"),
        ["ES"] = Set("el la los las un una unos unas y o pero es son era ser estoy está estás yo tú él ella nosotros vosotros ellos " +
                     "mi mis tu tus su sus este esta esto estos de del al en con por para no sí si que qué quién dónde cuándo cómo " +
                     "muy todo todos bien gracias hola vale porque también más menos puedo quiero hay vamos " +
                     "como eres soy somos quien quién verdad esto esta eso así mas ti lo nos sin entonces ahora aquí " +
                     "algo nada tan hoy solo sólo cuando donde tengo tienes hace hacer voy vas estamos mucho muchísimo muchisimo " +
                     "pues bueno claro oye mira vale guapa guapo cariño amor también tú"),
        ["DE"] = Set("der die das ein eine und oder aber ist sind war sein bin bist ich du er sie es wir ihr mich mein dein " +
                     "nicht kein ja nein mit von zu im am für auf aus wie was wer wo wann warum sehr alles gut danke hallo bitte " +
                     "auch noch schon mal kann will muss gibt den dem einen einem einer nichts habe hab hast haben " +
                     "dich dir mir uns euch sich doch nur jetzt heute hier dann wenn weil dass ob gerne gern lieb schatz süß " +
                     "ganz viel mehr bisschen echt wirklich halt eben denn wieder immer nie geht komm kommst kommt mach macht " +
                     "weiß weiss bis nach über ohne ihm ihn ihnen nee jo genau stimmt klar vielleicht morgen gleich " +
                     "möchte würde wäre hätte bitte tschüss liebe grüße boah krass geil mädel"),
        ["IT"] = Set("il lo la i gli le un uno una e o ma è sono era essere io tu lui lei noi voi loro mio mia tuo tua suo sua " +
                     "di del della in con per su non sì che chi dove quando perché come molto tutto bene grazie ciao anche più"),
        ["PT"] = Set("o a os as um uma e ou mas é são era ser estou está eu você ele ela nós eles meu minha seu sua de do da " +
                     "em no na com por para não sim que quem onde quando porque como muito tudo bem obrigado obrigada olá também mais"),
        // Russian: Cyrillic, plus "translit" (FFXIV chat cannot show Cyrillic, so Russians type in Latin letters).
        // Short translit words that are also words elsewhere (da, net, ne, ya, ty, nu, tut...) are left out.
        ["RU"] = Set("и в не на я что он как а то это все она так но да ты у же вы за бы по только мне было вот от меня еще ещё нет " +
                     "из ему теперь когда даже ну если уже или быть был до вас опять вам ведь там потом себя ничего может они тут " +
                     "где есть надо для мы тебя их чем тоже себе будет тогда кто потому какой здесь мой чтобы сейчас можно " +
                     "привет спасибо пожалуйста пока хорошо конечно давай люблю тебе очень сегодня завтра почему " +
                     "privet privetik kak dela chto shto eto menya tebya menja tebja tebe mne spasibo pozhaluysta pozhalujsta " +
                     "pozhalusta poka horosho khorosho harasho nichego segodnya sevodnya zavtra seychas sejchas sechas kogda pochemu " +
                     "potomu tozhe tozhe ochen davay davai poshli idem dobroe utro vecher spokoynoy spokojnoj lyublyu ljublju " +
                     "ladno konechno mozhno nado khochu hochu znayu znaju vse vsem kto gde zdes chem togda esche eshche yeshche uzhe " +
                     "rebyata rebjata mozhet budu budet byl byla bylo menja svoy moy moya tvoy tvoya ego eyo nas vas sps pzh kst " +
                     "normalno normas krasivo krasivaya milaya milyy zaya kotik solnyshko"),
    };

    /// <summary>Common MMO shorthand that carries no language information.</summary>
    private static readonly HashSet<string> Neutral = Set(
        "gg ty tyvm thx lol lmao rofl ok okay k kk o/ \\o o7 brb afk gl hf glhf wp np gn gm lfg lfm dps tank healer heal " +
        "mt ot aoe tb buff debuff lb lb3 pf dc rdy ready xd haha hahaha mdr ptdr ff ffxiv fc ls cwls ex savage ult");

    [GeneratedRegex(@"[\p{L}']+", RegexOptions.CultureInvariant)]
    private static partial Regex WordRegex();

    private static HashSet<string> Set(string words) =>
        new(words.Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);

    /// <summary>MMO shorthand that carries no language information (gg, dps, tank...).</summary>
    public static bool IsNeutralWord(string word) => Neutral.Contains(word);

    /// <summary>True when the text has no real words worth translating ("gg", "o/", emotes, numbers...).</summary>
    public static bool IsTrivial(string text)
    {
        var words = WordRegex().Matches(text).Select(m => m.Value).ToList();
        if (words.Count == 0)
            return true;
        return words.All(w => Neutral.Contains(w) || w.Length == 1);
    }

    /// <summary>
    /// Weaker guess for short fragments: the best-scoring candidate even without a clear margin
    /// (still null on ties or when nothing matches).
    /// </summary>
    public static string? WeakGuess(string text, IReadOnlyCollection<string> candidates)
    {
        if (IsMostlyCyrillic(text))
            return candidates.Contains("RU") ? "RU" : null;
        var words = WordRegex().Matches(text.ToLowerInvariant()).Select(m => m.Value.Trim('\'')).Where(w => w.Length > 0).ToList();
        var scored = StopWords.Where(kv => candidates.Contains(kv.Key))
                              .Select(kv => (Lang: kv.Key, Score: words.Count(w => kv.Value.Contains(w) || (w.Length > 1 && Slang.IsKnown(kv.Key, w)))))
                              .OrderByDescending(x => x.Score).ToList();
        if (scored.Count == 0 || scored[0].Score == 0 || (scored.Count > 1 && scored[1].Score == scored[0].Score))
            return null;
        return scored[0].Lang;
    }

    /// <summary>Guesses which of <paramref name="candidates"/> the text is written in, or null when unsure.</summary>
    public static string? Detect(string text, IReadOnlyCollection<string> candidates)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        // Japanese: kana is unambiguous.
        if (candidates.Contains("JA") && text.Any(c => c is >= '぀' and <= 'ヿ'))
            return "JA";

        // Cyrillic script: Russian (Ukrainian etc. are left to the engine when Russian is not a candidate).
        if (IsMostlyCyrillic(text))
            return candidates.Contains("RU") ? "RU" : null;

        var words = WordRegex().Matches(text.ToLowerInvariant())
                               .Select(m => m.Value.Trim('\''))
                               .Where(w => w.Length > 0)
                               .ToList();
        if (words.Count == 0)
            return null;

        var scores = new Dictionary<string, double>();
        // Score every language we know, not only the candidates: a Spanish message must not be
        // mistaken for French just because Spanish is not part of the configured pair.
        foreach (var (lang, stop) in StopWords)
        {
            double score = words.Count(w => stop.Contains(w) || (w.Length > 1 && Slang.IsKnown(lang, w)));
            // Elided forms (j'ai, c'est, l'eau, qu'il...) are a strong French signal.
            if (lang == "FR")
                score += 1.5 * words.Count(w => w.Length > 2 && w[1] == '\'' && "jcdlmnstq".Contains(w[0]));
            if (lang == "FR")
                score += 0.5 * text.Count(c => "éèêëàâçîïôûùœ".Contains(char.ToLowerInvariant(c)));
            if (lang == "ES")
                score += 0.5 * text.Count(c => "ñáíóú¿¡".Contains(char.ToLowerInvariant(c)));
            if (lang == "DE")
                score += 0.5 * text.Count(c => "äöüß".Contains(char.ToLowerInvariant(c)));
            scores[lang] = score;
        }

        if (scores.Count == 0)
            return null;

        var ordered = scores.OrderByDescending(kv => kv.Value).ToList();
        var best = ordered[0];
        if (!candidates.Contains(best.Key))
            return null;
        var second = ordered.Count > 1 ? ordered[1].Value : 0;

        // Require a clear winner; otherwise let the engine decide.
        if (best.Value >= 1 && best.Value >= second * 2 && best.Value - second >= 1)
            return best.Key;
        return null;
    }

    /// <summary>More Cyrillic letters than Latin ones.</summary>
    private static bool IsMostlyCyrillic(string text)
    {
        var cyr = text.Count(Translit.IsCyrillic);
        var latin = text.Count(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z');
        return cyr > 0 && cyr >= latin;
    }
}
