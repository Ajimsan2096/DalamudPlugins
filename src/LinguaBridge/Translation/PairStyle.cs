using System.Collections.Generic;
using System.Linq;

namespace LinguaBridge.Translation;

/// <summary>
/// Hand-tuned guidance for the directions this group uses most (ES>FR, FR>EN, EN>FR, plus ES>EN and FR>ES):
/// style rules per target language, worked examples (few-shot) for AI engines, a short style hint for DeepL,
/// and a recommended starter glossary.
/// </summary>
public static class PairStyle
{
    /// <summary>How natural chat sounds in each target language.</summary>
    private static readonly Dictionary<string, string> TargetRules = new()
    {
        ["FR"] =
            "French style: write like a native French player chatting, not like a textbook. Use 'tu' between players unless " +
            "the scene clearly uses 'vous'. Natural spoken forms are welcome (\"je sais pas\", \"t'es\", \"y a\"), " +
            "correct gender agreement for each character, French word order, no anglicisms when a common French word " +
            "exists. Terms of endearment: mon cœur, mon amour, bébé, ma belle, mon chéri / ma chérie (match the gender " +
            "of the person addressed). Laughter: \"haha\" or \"mdr\". Keep French spacing before ? ! : ;",
        ["EN"] =
            "English style: casual, idiomatic, native-sounding chat English (contractions, natural phrasal verbs). " +
            "French affectionate third person addressed to the listener (\"c'est qu'il est mignon\") becomes second " +
            "person (\"aren't you cute\"). Tag questions for French \"dis-moi\" / \"hein\" (\"..., aren't you?\"). " +
            "Endearments: honey, babe, darling, sweetheart, love. \"mdr/ptdr\" -> \"lol/lmao\".",
        ["ES"] =
            "Spanish style: natural, informal Spain Spanish chat (tú, contractions of speech), correct gender agreement, " +
            "opening ¿ ¡ marks. Endearments: cariño, amor, bebé, guapa/guapo, mi vida.",
    };

    /// <summary>Worked examples per direction (source, target) -> (input, ideal output).</summary>
    private static readonly Dictionary<(string, string), (string In, string Out)[]> Examples = new()
    {
        [("ES", "FR")] = new[]
        {
            ("*Sonrío y te abrazo por la espalda* ¿Me echaste de menos, cariño?",
             "*Je souris et je te prends dans mes bras par derrière* Je t'ai manqué, mon cœur ?"),
            ("No te voy a mentir, me vuelves loco jajaja",
             "Je vais pas te mentir, tu me rends fou hahaha"),
            ("Estoy disfrutando esto muchísimo, de verdad *acaricio tu pelo con cariño*",
             "J'adore ce moment, vraiment *je caresse tes cheveux avec tendresse*"),
        },
        [("FR", "EN")] = new[]
        {
            ("C'est qu'il est mignon aujourd'hui ! *rigole doucement*",
             "Aren't you cute today! *giggles softly*"),
            ("Tu te fais plaisir dis-moi ^^",
             "Enjoying yourself, aren't you? ^^"),
            ("jsp, je te dis ça plus tard tkt",
             "idk, I'll let you know later, don't worry"),
        },
        [("EN", "FR")] = new[]
        {
            ("*pulls you closer, resting my forehead against yours* I missed you so much, babe.",
             "*je t'attire contre moi et pose mon front contre le tien* Tu m'as tellement manqué, bébé."),
            ("Gotcha, brb in 5",
             "Ok, je reviens dans 5 min"),
            ("You're not allowed to leave, you know that right?",
             "T'as pas le droit de partir, tu le sais, hein ?"),
        },
        [("ES", "EN")] = new[]
        {
            ("Solo contigo, ¡un privilegio especial! *te guiño un ojo*",
             "Only with you, a special privilege! *winks at you*"),
        },
        [("FR", "ES")] = new[]
        {
            ("Je bouge pas d'ici mon chéri *se blottit contre toi*",
             "No me muevo de aquí, cariño *se acurruca contra ti*"),
        },
    };

    public static string? RulesFor(string targetCode) => TargetRules.GetValueOrDefault(targetCode);

    /// <summary>Examples for this direction; with an unknown source, every example into the target.</summary>
    public static IEnumerable<(string Source, string In, string Out)> ExamplesFor(string? sourceCode, string targetCode) =>
        Examples.Where(kv => kv.Key.Item2 == targetCode && (sourceCode == null || kv.Key.Item1 == sourceCode))
                .SelectMany(kv => kv.Value.Select(e => (kv.Key.Item1, e.In, e.Out)))
                .Take(4);

    /// <summary>Short style hint for DeepL's context field (context is not billed).</summary>
    public static string DeepLHint(string targetCode) => targetCode switch
    {
        "FR" => "Target style: natural spoken French chat between close friends or partners (tu, ça, y a, je sais pas).",
        "EN" => "Target style: natural casual English chat (contractions, idioms), not literal.",
        "ES" => "Target style: natural informal Spanish chat (tú).",
        _ => string.Empty,
    };

    /// <summary>Recommended starter glossary: endearments and official FFXIV names that differ between EN and FR.</summary>
    public static readonly (string From, string To, string Source, string Target)[] RecommendedGlossary =
    {
        // Endearments / chat
        ("FR", "EN", "mon cœur", "sweetheart"), ("FR", "EN", "mon coeur", "sweetheart"), ("FR", "EN", "mon amour", "my love"),
        ("FR", "EN", "bisous", "kisses"), ("FR", "EN", "mdr", "lol"), ("FR", "EN", "ptdr", "lmao"),
        ("EN", "FR", "babe", "bébé"), ("EN", "FR", "sweetheart", "mon cœur"), ("EN", "FR", "my love", "mon amour"),
        ("EN", "FR", "queen", "ma reine"), ("EN", "FR", "princess", "princesse"), ("EN", "FR", "lol", "mdr"),
        ("ES", "FR", "amor", "mon amour"), ("ES", "FR", "mi amor", "mon amour"), ("ES", "FR", "bebé", "bébé"),
        ("ES", "FR", "mi vida", "ma vie"), ("ES", "FR", "princesa", "princesse"), ("ES", "FR", "reina", "ma reine"),
        ("ES", "FR", "jajaja", "hahaha"), ("ES", "FR", "jaja", "haha"),
        // FFXIV jobs and terms (official EN <-> FR names)
        ("EN", "FR", "Dark Knight", "Chevalier noir"), ("FR", "EN", "Chevalier noir", "Dark Knight"),
        ("EN", "FR", "White Mage", "Mage blanc"), ("FR", "EN", "Mage blanc", "White Mage"),
        ("EN", "FR", "Black Mage", "Mage noir"), ("FR", "EN", "Mage noir", "Black Mage"),
        ("EN", "FR", "Red Mage", "Mage rouge"), ("FR", "EN", "Mage rouge", "Red Mage"),
        ("EN", "FR", "Gunbreaker", "Pistosabreur"), ("FR", "EN", "Pistosabreur", "Gunbreaker"),
        ("EN", "FR", "Reaper", "Faucheur"), ("FR", "EN", "Faucheur", "Reaper"),
        ("EN", "FR", "Scholar", "Érudit"), ("FR", "EN", "Érudit", "Scholar"),
        ("EN", "FR", "Astrologian", "Astromancien"), ("FR", "EN", "Astromancien", "Astrologian"),
        ("EN", "FR", "Dragoon", "Chevalier dragon"), ("FR", "EN", "Chevalier dragon", "Dragoon"),
        ("EN", "FR", "Summoner", "Invocateur"), ("FR", "EN", "Invocateur", "Summoner"),
        ("EN", "FR", "Machinist", "Machiniste"), ("FR", "EN", "Machiniste", "Machinist"),
        ("EN", "FR", "Dancer", "Danseur"), ("FR", "EN", "Danseur", "Dancer"),
        ("EN", "FR", "Warrior", "Guerrier"), ("FR", "EN", "Guerrier", "Warrior"),
        ("EN", "FR", "Monk", "Moine"), ("FR", "EN", "Moine", "Monk"),
        ("EN", "FR", "Bard", "Barde"), ("FR", "EN", "Barde", "Bard"),
        ("EN", "FR", "Free Company", "Compagnie libre"), ("FR", "EN", "Compagnie libre", "Free Company"),
        ("EN", "FR", "aether", "éther"), ("FR", "EN", "éther", "aether"),
    };
}
