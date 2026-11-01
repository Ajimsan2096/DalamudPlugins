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
                     "muy todo todos bien gracias hola vale porque también más menos puedo quiero hay vamos"),
        ["DE"] = Set("der die das ein eine und oder aber ist sind war sein bin bist ich du er sie es wir ihr mich mein dein " +
                     "nicht kein ja nein mit von zu im am für auf aus wie was wer wo wann warum sehr alles gut danke hallo bitte " +
                     "auch noch schon mal kann will muss gibt"),
        ["IT"] = Set("il lo la i gli le un uno una e o ma è sono era essere io tu lui lei noi voi loro mio mia tuo tua suo sua " +
                     "di del della in con per su non sì che chi dove quando perché come molto tutto bene grazie ciao anche più"),
        ["PT"] = Set("o a os as um uma e ou mas é são era ser estou está eu você ele ela nós eles meu minha seu sua de do da " +
                     "em no na com por para não sim que quem onde quando porque como muito tudo bem obrigado obrigada olá também mais"),
    };

    /// <summary>Common MMO shorthand that carries no language information.</summary>
    private static readonly HashSet<string> Neutral = Set(
        "gg ty tyvm thx lol lmao rofl ok okay k kk o/ \\o o7 brb afk gl hf glhf wp np gn gm lfg lfm dps tank healer heal " +
        "mt ot aoe tb buff debuff lb lb3 pf dc rdy ready xd haha hahaha mdr ptdr ff ffxiv fc ls cwls ex savage ult");

    [GeneratedRegex(@"[\p{L}']+", RegexOptions.CultureInvariant)]
    private static partial Regex WordRegex();

    private static HashSet<string> Set(string words) =>
        new(words.Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);

    /// <summary>True when the text has no real words worth translating ("gg", "o/", emotes, numbers...).</summary>
    public static bool IsTrivial(string text)
    {
        var words = WordRegex().Matches(text).Select(m => m.Value).ToList();
        if (words.Count == 0)
            return true;
        return words.All(w => Neutral.Contains(w) || w.Length == 1);
    }

    /// <summary>Guesses which of <paramref name="candidates"/> the text is written in, or null when unsure.</summary>
    public static string? Detect(string text, IReadOnlyCollection<string> candidates)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        // Japanese: kana is unambiguous.
        if (candidates.Contains("JA") && text.Any(c => c is >= '぀' and <= 'ヿ'))
            return "JA";

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
}
