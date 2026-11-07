using System.Collections.Generic;
using System.Linq;

namespace LinguaBridge.Translation;

/// <summary>
/// Hand-tuned guidance for the directions this group uses most (ES>FR, FR>EN, EN>FR, ES>DE, DE>EN, plus
/// ES>EN, FR>ES, EN>DE and DE>ES):
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
        ["DE"] =
            "German style: write like a native German player chatting, not like a textbook or a subtitle. Use 'du' " +
            "(dich, dir, dein) between players, never 'Sie', unless the scene clearly uses formal address. Natural " +
            "spoken forms are welcome (\"hab\", \"is\", \"'ne\", \"grad\", \"mal\", \"echt\", \"halt\", \"oder?\" as a tag), " +
            "but keep the grammar right: correct case (Akkusativ/Dativ), article and adjective endings matching each " +
            "character's gender, verb in second position and at the end of subordinate clauses (weil, dass, wenn, ob). " +
            "Use modal particles (doch, ja, mal, halt, schon) where a native would. German gamers keep common English " +
            "game words (Tank, Heal, DPS, Raid, Dungeon, Loot, Mount, afk, gg). Terms of endearment: Schatz, Liebling, " +
            "Süße / Süßer, mein Herz, Hase, Maus. Laughter: \"haha\" or \"xD\". Interjections: boah, krass, oh Mann, na ja. " +
            "Capitalise nouns.",
        ["ES"] =
            "Spanish style: natural, informal Spain Spanish chat (tú, contractions of speech), correct gender agreement, " +
            "opening ¿ ¡ marks. Endearments: cariño, amor, bebé, guapa/guapo, mi vida.",
    };

    /// <summary>How to read chat written in each source language (slang and particles engines often get wrong).</summary>
    private static readonly Dictionary<string, string> SourceNotes = new()
    {
        ["DE"] =
            "Reading German chat: modal particles (doch, ja, halt, eben, mal, schon, wohl, etwa) carry tone, not " +
            "meaning; render the tone (\"Komm doch mal her\" -> \"Come here, will you\"; \"Das ist ja süß\" -> \"Aw, " +
            "that's so sweet\"), never translate them word for word. \"Na?\" / \"Na du?\" is a casual greeting " +
            "(\"Hey you\" / \"Hey, what's up?\"). A trailing \"oder?\" / \"ne?\" / \"gell?\" is a tag question (\"right?\"). " +
            "\"Bock haben\" = to feel like, \"krass\" = crazy/wow, \"Alter\" = dude, \"geil\" = awesome (or sexy, by " +
            "context), \"Digga\" = bro, \"mega\" = super, \"voll\" = totally, \"Feierabend\" = done for the day, " +
            "\"Mahlzeit\" = a midday greeting. \"Schatz\", \"Maus\", \"Hase\", \"Süße\" addressed to someone are endearments " +
            "(babe, sweetie, honey), not treasure / mouse / rabbit. German Perfekt (\"ich hab gegessen\") usually means " +
            "a simple past in English. Separable verbs can split across the sentence (\"Ich rufe dich morgen an\").",
        ["ES"] =
            "Reading Spain Spanish chat: \"tío/tía\" addressed to someone = dude / girl (not uncle/aunt), \"vale\" = okay, " +
            "\"venga\" = come on / alright, \"guay\" = cool, \"mola\" = it's cool, \"qué fuerte\" = no way / that's " +
            "crazy, \"flipar\" = to be amazed, \"me pone\" = turns me on (or puts me in a mood, by context), \"echar de " +
            "menos\" = to miss someone, \"ser un cielo / un sol\" = to be a sweetheart, \"hacer gracia\" = to amuse. " +
            "\"jajaja\" is laughter (-> haha / hahaha), \"xd\" is an emoticon. Spanish often drops the subject pronoun: " +
            "infer the person from the verb ending and the context.",
        ["FR"] =
            "Reading French chat: the affectionate third person (\"c'est qu'il est mignon\") usually speaks to the " +
            "listener. \"mdr\" / \"ptdr\" are laughter.",
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
        [("ES", "DE")] = new[]
        {
            ("*Sonrío y te abrazo por la espalda* ¿Me echaste de menos, cariño?",
             "*Ich lächle und umarme dich von hinten* Hast du mich vermisst, Schatz?"),
            ("Tía, no me lo puedo creer jajaja, ¡qué fuerte!",
             "Mädel, ich fass es nicht hahaha, wie krass!"),
            ("Vale, ahora vuelvo, no te muevas de aquí, ¿eh? *te guiña un ojo*",
             "Okay, bin gleich wieder da, rühr dich nicht von der Stelle, ja? *zwinkert dir zu*"),
        },
        [("DE", "EN")] = new[]
        {
            ("Na, hast du mich etwa vermisst? *grinst frech*",
             "Well, did you miss me or what? *grins cheekily*"),
            ("Ich hab grad echt keinen Bock mehr, lass uns morgen weitermachen, ok?",
             "I'm really not feeling it anymore, let's pick this up tomorrow, ok?"),
            ("Das ist ja voll süß von dir, hdl ^^",
             "Aww, that's so sweet of you, love you ^^"),
        },
        [("EN", "DE")] = new[]
        {
            ("*pulls you closer, resting my forehead against yours* I missed you so much, babe.",
             "*ich ziehe dich näher an mich und lehne meine Stirn an deine* Ich hab dich so vermisst, Schatz."),
            ("Gotcha, brb in 5",
             "Alles klar, bin in 5 Min wieder da"),
            ("You're not allowed to leave, you know that right?",
             "Du darfst nicht gehen, das weißt du doch, oder?"),
        },
        [("DE", "ES")] = new[]
        {
            ("Ich rühr mich hier nicht weg, Schatz *kuschelt sich an dich*",
             "No me muevo de aquí, cariño *se acurruca contra ti*"),
            ("Haha, du bist echt süß, weißt du das eigentlich?",
             "Jajaja, eres un cielo, ¿lo sabías?"),
        },
    };

    public static string? RulesFor(string targetCode) => TargetRules.GetValueOrDefault(targetCode);

    /// <summary>Notes on reading the source language, or null when unknown / none.</summary>
    public static string? SourceNotesFor(string? sourceCode) =>
        sourceCode == null ? null : SourceNotes.GetValueOrDefault(sourceCode);

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
        "DE" => "Target style: natural casual German chat between friends or partners (du, not Sie; hab, mal, grad, echt), " +
                "correct case and gender endings, nouns capitalised.",
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
        ("ES", "DE", "mi amor", "mein Schatz"), ("ES", "DE", "mi vida", "mein Herz"), ("ES", "DE", "bebé", "Baby"),
        ("ES", "DE", "princesa", "Prinzessin"), ("ES", "DE", "jajaja", "hahaha"), ("ES", "DE", "jaja", "haha"),
        ("DE", "EN", "mein Schatz", "babe"), ("DE", "EN", "Liebling", "darling"), ("DE", "EN", "hab dich lieb", "love you"),
        ("DE", "EN", "hab dich ganz doll lieb", "love you so much"), ("DE", "EN", "Küsschen", "kisses"),
        ("EN", "DE", "babe", "Schatz"), ("EN", "DE", "sweetheart", "Liebling"), ("EN", "DE", "my love", "mein Schatz"),
        ("EN", "DE", "princess", "Prinzessin"),
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
        // FFXIV jobs and terms (official EN <-> DE names)
        ("EN", "DE", "Dark Knight", "Dunkelritter"), ("DE", "EN", "Dunkelritter", "Dark Knight"),
        ("EN", "DE", "Gunbreaker", "Revolverklinge"), ("DE", "EN", "Revolverklinge", "Gunbreaker"),
        ("EN", "DE", "Warrior", "Krieger"), ("DE", "EN", "Krieger", "Warrior"),
        ("EN", "DE", "White Mage", "Weißmagier"), ("DE", "EN", "Weißmagier", "White Mage"),
        ("EN", "DE", "Black Mage", "Schwarzmagier"), ("DE", "EN", "Schwarzmagier", "Black Mage"),
        ("EN", "DE", "Red Mage", "Rotmagier"), ("DE", "EN", "Rotmagier", "Red Mage"),
        ("EN", "DE", "Scholar", "Gelehrter"), ("DE", "EN", "Gelehrter", "Scholar"),
        ("EN", "DE", "Astrologian", "Astrologe"), ("DE", "EN", "Astrologe", "Astrologian"),
        ("EN", "DE", "Summoner", "Beschwörer"), ("DE", "EN", "Beschwörer", "Summoner"),
        ("EN", "DE", "Reaper", "Schnitter"), ("DE", "EN", "Schnitter", "Reaper"),
        ("EN", "DE", "Machinist", "Maschinist"), ("DE", "EN", "Maschinist", "Machinist"),
        ("EN", "DE", "Dancer", "Tänzer"), ("DE", "EN", "Tänzer", "Dancer"),
        ("EN", "DE", "Monk", "Mönch"), ("DE", "EN", "Mönch", "Monk"),
        ("EN", "DE", "Bard", "Barde"), ("DE", "EN", "Barde", "Bard"),
        ("EN", "DE", "Pictomancer", "Piktomant"), ("DE", "EN", "Piktomant", "Pictomancer"),
        ("EN", "DE", "Free Company", "Freie Gesellschaft"), ("DE", "EN", "Freie Gesellschaft", "Free Company"),
        ("EN", "DE", "Limit Break", "Limitrausch"), ("DE", "EN", "Limitrausch", "Limit Break"),
        ("EN", "DE", "aether", "Äther"), ("DE", "EN", "Äther", "aether"),
    };
}
