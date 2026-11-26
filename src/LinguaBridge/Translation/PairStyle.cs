using System.Collections.Generic;
using System.Linq;

namespace LinguaBridge.Translation;

/// <summary>
/// Hand-tuned guidance for the directions this group uses most (ES>FR, FR>EN, EN>FR, ES>DE, DE>EN, plus
/// ES>EN, FR>ES, EN>DE, DE>ES and Dutch: NL>EN, EN>NL, ES>NL, NL>ES):
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
        ["NL"] =
            "Dutch style: write like a native Dutch player chatting, not like a textbook or a subtitle. Use 'je' / 'jij' / " +
            "'jou' between players, never 'u', unless the scene clearly uses formal address. Natural spoken forms are welcome " +
            "(\"'k\", \"'t\", \"m'n\", \"z'n\", \"da's\", \"ff\" for even), but keep the grammar right: de/het articles, " +
            "adjective -e endings, verb in second position and at the end of subordinate clauses (omdat, dat, als, of), " +
            "separable verbs split correctly (\"ik bel je morgen op\"). Use the particles a native would (even, maar, " +
            "toch, wel, hoor, joh, hè) and diminutives for warmth (schatje, momentje, dingetje). There is no Dutch game " +
            "client: Dutch players use the English names of jobs, places, items and mechanics (tank, healer, DPS, raid, " +
            "dungeon, loot, mount, Free Company, Limit Break, Dark Knight) and say \"pullen\", \"wipen\", \"ressen\". " +
            "Terms of endearment: schat, schatje, lieverd, liefje, lief. Laughter: \"haha\" or \"hihi\". " +
            "Interjections: jeetje, tjonge, wauw, gaaf, vet, balen, jammer.",
        ["ES"] =
            "Spanish style: natural, informal Spain Spanish chat (tú, contractions of speech), correct gender agreement, " +
            "opening ¿ ¡ marks. Endearments: cariño, amor, bebé, guapa/guapo, mi vida.",
        ["JA"] =
            "Japanese style: natural casual Japanese chat between players (タメ口: plain forms like だよ, じゃん, かな, ね), " +
            "not keigo or textbook です/ます, unless the scene is clearly formal or a character speaks politely. Drop " +
            "pronouns where a native would (no あなた for \"you\"). Japanese players write game terms in katakana or " +
            "short forms (タンク, ヒーラー, DPS, レイド, ID, マウント, FC, LB). Laughter: \"笑\" or \"w\". Keep emoticons " +
            "and *actions*. Endearments sparingly and naturally (ダーリン, ハニー, 〜ちゃん / 〜くん after a name).",
        ["IT"] =
            "Italian style: natural, informal Italian chat between players (tu, not Lei, unless the scene is formal), " +
            "correct gender and number agreement for each character, natural spoken forms (\"dai\", \"boh\", \"vabbè\", " +
            "\"ci sta\"). Italian gamers keep common English game words (tank, healer, DPS, raid, dungeon, loot, mount). " +
            "Endearments: tesoro, amore, cucciolo / cucciola, piccola. Laughter: \"ahah\" or \"ahahah\".",
        ["PT"] =
            "Portuguese style: natural, informal Brazilian Portuguese chat between players (você, \"a gente\", \"tá\", " +
            "\"pra\"), correct gender agreement for each character. Brazilian gamers keep common English game words (tank, " +
            "healer, DPS, raid, dungeon, loot, mount). Endearments: amor, meu bem, linda / lindo, fofa / fofo. " +
            "Laughter: \"kkkk\" or \"haha\".",
        ["RU"] =
            "Russian style: natural casual Russian chat between players (ты, not вы, unless the scene is formal), correct " +
            "case endings and verb/adjective gender for each character (он сделал / она сделала). Russian gamers keep " +
            "common game words (танк, хил, дпс, рейд, данж, лут, маунт, афк, гг). Endearments: милая / милый, солнышко, " +
            "зайка, котик, любимая / любимый. Laughter: \"ахаха\" or \"хаха\". Write in Cyrillic (it is converted for the game).",
    };

    /// <summary>How to read chat written in each source language (slang and particles engines often get wrong).</summary>
    private static readonly Dictionary<string, string> SourceNotes = new()
    {
        ["RU"] =
            "Reading Russian chat: FFXIV cannot show Cyrillic, so Russian players type in Latin letters (\"privet, kak " +
            "dela\"); the text may have been converted back to Cyrillic automatically and can contain small spelling " +
            "errors from that, read it for meaning. \"спс\" = thanks, \"пж\" = please, \"мб\" = maybe, \"хз\" = no idea, " +
            "\"норм\" = fine, \"го\" = let's go, \"ахах\" = haha, \"зай\" / \"котик\" / \"солнышко\" addressed to someone " +
            "are endearments (babe, sweetie), not animals / sun.",
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
        ["NL"] =
            "Reading Dutch chat (Netherlands and Flemish): particles (even, maar, toch, wel, hoor, joh, nou, hè) carry tone, " +
            "not meaning; render the tone (\"Kom maar even hier\" -> \"Come here a sec\"; \"Dat is toch leuk!\" -> \"Isn't " +
            "that fun!\"), never word for word. A trailing \"hè?\" / \"toch?\" / \"of niet?\" is a tag question (\"right?\"). " +
            "\"lekker\" = nice / great (not only tasty: \"lekker bezig\" = nice going), \"gezellig\" = cosy / fun to be " +
            "with, \"zin hebben in\" = to feel like, \"vet\" / \"gaaf\" = cool, \"balen\" = bummer, \"jammer\" = too bad, " +
            "\"doei\" = bye, \"ff\" = even (a moment, quickly), \"idd\" = indeed, \"gwn\" = just, \"wss\" = probably, " +
            "\"mss\" = maybe, \"sws\" = anyway / definitely, \"mattie\" / \"gozer\" = buddy / guy, \"meid\" = girl. " +
            "Flemish: \"ge\" / \"gij\" = you, \"da\" = dat, \"nie\" = niet, \"amai\" = wow, \"allez\" = come on, " +
            "\"plezant\" = fun, \"goesting\" = to feel like, \"efkes\" = a moment, \"seffens\" = in a bit, " +
            "\"wablief\" = pardon?. \"Schat\", \"schatje\", \"lieverd\", \"liefje\" addressed to someone are endearments " +
            "(babe, sweetie, honey), not treasure. Diminutives (-je, -tje) often add warmth, not smallness " +
            "(\"momentje\" = just a moment). Dutch present tense often means the future (\"ik kom morgen\" = I'll come tomorrow).",
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
        [("NL", "EN")] = new[]
        {
            ("Hé schatje, heb je me gemist? *grijnst ondeugend*",
             "Hey babe, did you miss me? *grins mischievously*"),
            ("Ik heb er echt geen zin meer in, zullen we morgen ff verder gaan?",
             "I'm really not feeling it anymore, shall we pick this up tomorrow?"),
            ("Das echt lief van je, dankjewel hoor ^^",
             "Aww, that's really sweet of you, thank you ^^"),
        },
        [("EN", "NL")] = new[]
        {
            ("*pulls you closer, resting my forehead against yours* I missed you so much, babe.",
             "*trek je dichter naar me toe en leg mijn voorhoofd tegen het jouwe* Ik heb je zo gemist, schatje."),
            ("Gotcha, brb in 5",
             "Oké, ben over 5 min terug"),
            ("You're not allowed to leave, you know that right?",
             "Je mag niet weggaan, dat weet je toch?"),
        },
        [("ES", "NL")] = new[]
        {
            ("*Sonrío y te abrazo por la espalda* ¿Me echaste de menos, cariño?",
             "*Ik glimlach en sla mijn armen van achteren om je heen* Heb je me gemist, schat?"),
            ("Tía, no me lo puedo creer jajaja",
             "Meid, ik geloof het gewoon niet hahaha"),
        },
        [("NL", "ES")] = new[]
        {
            ("Ik ga hier niet weg, schat *kruipt tegen je aan*",
             "No me muevo de aquí, cariño *se acurruca contra ti*"),
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
        "JA" => "Target style: natural casual Japanese chat between players (plain forms, not keigo), game terms in katakana.",
        "IT" => "Target style: natural informal Italian chat between friends (tu, not Lei).",
        "PT" => "Target style: natural informal Brazilian Portuguese chat between friends (você, tá, pra).",
        "RU" => "Target style: natural casual Russian chat between friends (ты, not вы), correct case and gender endings.",
        "NL" => "Target style: natural casual Dutch chat between friends or partners (je/jij, not u; even, hoor, toch), " +
                "English game terms kept (tank, healer, raid, dungeon).",
        "DE" => "Target style: natural casual German chat between friends or partners (du, not Sie; hab, mal, grad, echt), " +
                "correct case and gender endings, nouns capitalised.",
        _ => string.Empty,
    };

    /// <summary>
    /// Entries of older versions of the recommended glossary that did more harm than good (single words that are
    /// also plain nouns, job names forced into the masculine). Removed from existing configs on update, but only
    /// when they still have the original translation (the user did not edit them).
    /// </summary>
    public static readonly (string From, string To, string Source, string Target)[] RetiredGlossary =
    {
        ("EN", "FR", "sweetheart", "mon cœur"), ("EN", "FR", "queen", "ma reine"), ("ES", "FR", "amor", "mon amour"),
        ("ES", "FR", "reina", "ma reine"), ("EN", "DE", "sweetheart", "Liebling"), ("EN", "NL", "sweetheart", "lieverd"),
        ("EN", "FR", "Scholar", "Érudit"), ("FR", "EN", "Érudit", "Scholar"), ("EN", "FR", "Summoner", "Invocateur"),
        ("FR", "EN", "Invocateur", "Summoner"), ("EN", "FR", "Dancer", "Danseur"), ("FR", "EN", "Danseur", "Dancer"),
        ("EN", "FR", "Warrior", "Guerrier"), ("FR", "EN", "Guerrier", "Warrior"), ("EN", "FR", "Monk", "Moine"),
        ("FR", "EN", "Moine", "Monk"), ("EN", "FR", "Bard", "Barde"), ("FR", "EN", "Barde", "Bard"),
        ("EN", "FR", "Reaper", "Faucheur"), ("FR", "EN", "Faucheur", "Reaper"),
        ("EN", "DE", "Warrior", "Krieger"), ("DE", "EN", "Krieger", "Warrior"), ("EN", "DE", "Scholar", "Gelehrter"),
        ("DE", "EN", "Gelehrter", "Scholar"), ("EN", "DE", "Summoner", "Beschwörer"), ("DE", "EN", "Beschwörer", "Summoner"),
        ("EN", "DE", "Reaper", "Schnitter"), ("DE", "EN", "Schnitter", "Reaper"), ("EN", "DE", "Dancer", "Tänzer"),
        ("DE", "EN", "Tänzer", "Dancer"), ("EN", "DE", "Monk", "Mönch"), ("DE", "EN", "Mönch", "Monk"),
        ("EN", "DE", "Bard", "Barde"), ("DE", "EN", "Barde", "Bard"),
    };

    /// <summary>Recommended starter glossary: endearments and official FFXIV names that differ between EN and FR.</summary>
    public static readonly (string From, string To, string Source, string Target)[] RecommendedGlossary =
    {
        // Endearments / chat
        ("FR", "EN", "mon cœur", "sweetheart"), ("FR", "EN", "mon coeur", "sweetheart"), ("FR", "EN", "mon amour", "my love"),
        ("FR", "EN", "bisous", "kisses"), ("FR", "EN", "mdr", "lol"), ("FR", "EN", "ptdr", "lmao"),
        // Glossaries replace EVERY occurrence: only phrases that are always a pet name (with "my" / "mi"), never single
        // words that are also plain nouns ("amor" in "hacer el amor", "reina" in "la reina de Ul'dah").
        ("EN", "FR", "babe", "bébé"), ("EN", "FR", "my sweetheart", "mon cœur"), ("EN", "FR", "my love", "mon amour"),
        ("EN", "FR", "my queen", "ma reine"), ("EN", "FR", "princess", "princesse"), ("EN", "FR", "lol", "mdr"),
        ("ES", "FR", "mi amor", "mon amour"), ("ES", "FR", "bebé", "bébé"),
        ("ES", "FR", "mi vida", "ma vie"), ("ES", "FR", "princesa", "princesse"), ("ES", "FR", "mi reina", "ma reine"),
        ("ES", "FR", "jajaja", "hahaha"), ("ES", "FR", "jaja", "haha"),
        ("ES", "DE", "mi amor", "mein Schatz"), ("ES", "DE", "mi vida", "mein Herz"), ("ES", "DE", "bebé", "Baby"),
        ("ES", "DE", "princesa", "Prinzessin"), ("ES", "DE", "jajaja", "hahaha"), ("ES", "DE", "jaja", "haha"),
        ("DE", "EN", "mein Schatz", "babe"), ("DE", "EN", "Liebling", "darling"), ("DE", "EN", "hab dich lieb", "love you"),
        ("DE", "EN", "hab dich ganz doll lieb", "love you so much"), ("DE", "EN", "Küsschen", "kisses"),
        ("EN", "DE", "babe", "Schatz"), ("EN", "DE", "my sweetheart", "mein Liebling"), ("EN", "DE", "my love", "mein Schatz"),
        ("EN", "DE", "princess", "Prinzessin"),
        ("NL", "EN", "schatje", "babe"), ("NL", "EN", "lieverd", "sweetheart"), ("NL", "EN", "liefje", "sweetie"),
        ("NL", "EN", "dikke kus", "big kiss"), ("NL", "EN", "doei", "bye"),
        ("EN", "NL", "babe", "schatje"), ("EN", "NL", "my sweetheart", "mijn lieverd"), ("EN", "NL", "my love", "mijn lief"),
        ("EN", "NL", "princess", "prinses"),
        ("ES", "NL", "cariño", "schat"), ("ES", "NL", "mi amor", "mijn lief"), ("ES", "NL", "jajaja", "hahaha"),
        ("NL", "ES", "schatje", "cariño"), ("NL", "ES", "lieverd", "cariño"),
        // FFXIV jobs and terms (official EN <-> FR names)
        ("EN", "FR", "Dark Knight", "Chevalier noir"), ("FR", "EN", "Chevalier noir", "Dark Knight"),
        ("EN", "FR", "White Mage", "Mage blanc"), ("FR", "EN", "Mage blanc", "White Mage"),
        ("EN", "FR", "Black Mage", "Mage noir"), ("FR", "EN", "Mage noir", "Black Mage"),
        ("EN", "FR", "Red Mage", "Mage rouge"), ("FR", "EN", "Mage rouge", "Red Mage"),
        ("EN", "FR", "Gunbreaker", "Pistosabreur"), ("FR", "EN", "Pistosabreur", "Gunbreaker"),
        ("EN", "FR", "Astrologian", "Astromancien"), ("FR", "EN", "Astromancien", "Astrologian"),
        ("EN", "FR", "Dragoon", "Chevalier dragon"), ("FR", "EN", "Chevalier dragon", "Dragoon"),
        ("EN", "FR", "Machinist", "Machiniste"), ("FR", "EN", "Machiniste", "Machinist"),
        ("EN", "FR", "Pictomancer", "Pictomancien"), ("FR", "EN", "Pictomancien", "Pictomancer"),
        ("EN", "FR", "Viper", "Rôdeur vipère"), ("FR", "EN", "Rôdeur vipère", "Viper"),
        ("EN", "FR", "Blue Mage", "Mage bleu"), ("FR", "EN", "Mage bleu", "Blue Mage"),
        ("EN", "FR", "Limit Break", "Transcendance"), ("FR", "EN", "Transcendance", "Limit Break"),
        ("EN", "FR", "Free Company", "Compagnie libre"), ("FR", "EN", "Compagnie libre", "Free Company"),
        ("EN", "FR", "aether", "éther"), ("FR", "EN", "éther", "aether"),
        // FFXIV jobs and terms (official EN <-> DE names)
        ("EN", "DE", "Dark Knight", "Dunkelritter"), ("DE", "EN", "Dunkelritter", "Dark Knight"),
        ("EN", "DE", "Gunbreaker", "Revolverklinge"), ("DE", "EN", "Revolverklinge", "Gunbreaker"),
        ("EN", "DE", "White Mage", "Weißmagier"), ("DE", "EN", "Weißmagier", "White Mage"),
        ("EN", "DE", "Black Mage", "Schwarzmagier"), ("DE", "EN", "Schwarzmagier", "Black Mage"),
        ("EN", "DE", "Red Mage", "Rotmagier"), ("DE", "EN", "Rotmagier", "Red Mage"),
        ("EN", "DE", "Astrologian", "Astrologe"), ("DE", "EN", "Astrologe", "Astrologian"),
        ("EN", "DE", "Machinist", "Maschinist"), ("DE", "EN", "Maschinist", "Machinist"),
        ("EN", "DE", "Pictomancer", "Piktomant"), ("DE", "EN", "Piktomant", "Pictomancer"),
        ("EN", "DE", "Free Company", "Freie Gesellschaft"), ("DE", "EN", "Freie Gesellschaft", "Free Company"),
        ("EN", "DE", "Limit Break", "Limitrausch"), ("DE", "EN", "Limitrausch", "Limit Break"),
        ("EN", "DE", "aether", "Äther"), ("DE", "EN", "Äther", "aether"),
    };
}
