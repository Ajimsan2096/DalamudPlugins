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
                     "you're it's don't dont can't cant thanks thank please sorry want need know think let lets let's up out " +
                     "only after before again also really still never always every much many more other into because which while " +
                     "been being them him her his its our us mine anyone someone something nothing everyone " +
                     "against over under through around across behind toward towards"),
        ["FR"] = Set("le la les un une des et ou mais est sont était être suis es sommes êtes je tu il elle on nous vous ils elles " +
                     "me te se mon ma mes ton ta tes son sa ses notre votre leur ce cet cette ces de du au aux en dans sur pour avec " +
                     "par pas ne non oui qui que quoi où quand pourquoi comment très tout tous bien merci salut bonjour bonsoir " +
                     "c'est j'ai t'es y a ça ca aussi plus moins peux veux faut fait vais va allez désolé d'accord " +
                     "beaucoup trop peu encore toujours jamais rien personne quelque chose faire dire voir aller venir suis ont " +
                     "était mais donc alors parce comme chez sans vers avant après déjà ici demain soir nuit jour vraiment juste"),
        ["ES"] = Set("el la los las un una unos unas y o pero es son era ser estoy está estás yo tú él ella nosotros vosotros ellos " +
                     "mi mis tu tus su sus este esta esto estos de del al en con por para no sí si que qué quién dónde cuándo cómo " +
                     "muy todo todos bien gracias hola vale porque también más menos puedo quiero hay vamos " +
                     "como eres soy somos quien quién verdad esto esta eso así mas ti lo nos sin entonces ahora aquí " +
                     "algo nada tan hoy solo sólo cuando donde tengo tienes hace hacer voy vas estamos mucho muchísimo muchisimo " +
                     "pues bueno claro oye mira vale guapa guapo cariño amor también tú " +
                     // short Spanish words (not French): "ha sido", "otra vez"
                     "he ha han hemos otra otro vez ese esa esos esas alguien nadie nunca luego después siempre " +
                     "mañana noche juntos nuestro nuestra puedes quieres ahí allí hecho me te se le les"),
        ["DE"] = Set("der die das ein eine und oder aber ist sind war sein bin bist ich du er sie es wir ihr mich mein dein " +
                     "nicht kein ja nein mit von zu im am für auf aus wie was wer wo wann warum sehr alles gut danke hallo bitte " +
                     "auch noch schon mal kann will muss gibt den dem einen einem einer nichts habe hab hast haben " +
                     "dich dir mir uns euch sich doch nur jetzt heute hier dann wenn weil dass ob gerne gern lieb schatz süß " +
                     "ganz viel mehr bisschen echt wirklich halt eben denn wieder immer nie geht komm kommst kommt mach macht " +
                     "weiß weiss bis nach über ohne ihm ihn ihnen nee jo genau stimmt klar vielleicht morgen gleich " +
                     "möchte würde wäre hätte bitte tschüss liebe grüße boah krass geil mädel"),
        ["IT"] = Set("il lo la i gli le un uno una e o ma è sono era essere io tu lui lei noi voi loro mio mia tuo tua suo sua " +
                     "di del della in con per su non sì che chi dove quando perché come molto tutto bene grazie ciao anche più " +
                     "si mi ti ci vi dello degli delle nel nella nei negli sul sulla alla allo agli dal dalla tra fra " +
                     "questo questa questi quello quella sto sta stai stiamo hai ho abbiamo avete siamo siete stato stata " +
                     "ancora oggi stasera domani qualcuno qualcosa niente nulla fare puoi può vuoi voglio dobbiamo cosa " +
                     "prego scusa scusate allora però quindi proprio troppo amico amica tutti nostro nostra vostro vostra " +
                     "perche cosi gia piu boh dai al alle dopo pochi poco molti se posso devi devo"),
        ["PT"] = Set("o a os as um uma e ou mas é são era ser estou está eu você ele ela nós eles meu minha seu sua de do da " +
                     "em no na com por para não sim que quem onde quando porque como muito tudo bem obrigado obrigada olá também mais " +
                     "vocês nao voce tem têm ter fazer foi isso isto essa esse aqui agora depois ainda já pra pro nas pelo pela " +
                     "nosso nossa vai vou quero posso pode sempre hoje amanhã amanha noite oi tchau beleza então entao estava " +
                     "fica ficar obrigada demais tá né dessa desse nessa nesse muita muitos alguém dia me te se lhe vamos nos vez deu gente"),
        // Russian: Cyrillic, plus "translit" (FFXIV chat cannot show Cyrillic, so Russians type in Latin letters).
        // Short translit words shared with other languages (da, ne, nu, tut...) are left out; ty / ya / net are kept
        // (a tie with English "ty" or French "ya" just leaves the decision to the engine).
        ["RU"] = Set("и в не на я что он как а то это все она так но да ты у же вы за бы по только мне было вот от меня еще ещё нет " +
                     "из ему теперь когда даже ну если уже или быть был до вас опять вам ведь там потом себя ничего может они тут " +
                     "где есть надо для мы тебя их чем тоже себе будет тогда кто потому какой здесь мой чтобы сейчас можно " +
                     "привет спасибо пожалуйста пока хорошо конечно давай люблю тебе очень сегодня завтра почему " +
                     "ty ya net ot na v s k u i o ne za so dva tri opyat drugom drugoy bratan brat hz 4to 4ego privet privetik kak dela chto shto eto menya tebya menja tebja tebe mne spasibo pozhaluysta pozhalujsta " +
                     "pozhalusta poka horosho khorosho harasho nichego segodnya sevodnya zavtra seychas sejchas sechas kogda pochemu " +
                     "potomu tozhe tozhe ochen davay davai poshli idem dobroe utro vecher spokoynoy spokojnoj lyublyu ljublju " +
                     "ladno konechno mozhno nado khochu hochu znayu znaju vse vsem kto gde zdes chem togda esche eshche yeshche uzhe " +
                     "rebyata rebjata mozhet budu budet byl byla bylo menja svoy moy moya tvoy tvoya ego eyo nas vas sps pzh kst " +
                     "normalno normas krasivo krasivaya milaya milyy zaya kotik solnyshko tolko tak mogu ili budesh videt kruto takzhe tvoy svoy dlya bez pro sebya sebe dumayu kazhetsya davno skoro " +
                     "poydem idi vot otkuda zachem kuda nichego mozhesh znaesh hochesh delaesh vidish sdelat skazat tozhe vsyo"),
    };

    /// <summary>
    /// Gaming / chat jargon and interjections that carry no language information: a message made only of
    /// these is not translated, and they never count as a vote for a language (a lone "Yes!" or "RIP" in a
    /// Spanish message must not make it "mixed" with English).
    /// Words that are also real words or chat shorthand in a supported language are deliberately left out:
    /// ya (ES/FR), yo (ES), no, nu / da / net (RU), ns / tb-style ES shorthand, bb / nn (FR/DE), war (DE), hallo...
    /// </summary>
    private static readonly HashSet<string> Neutral = Set(
        // yes / agreement (also cute and elongated spellings: yush, yesh, yasss...)
        "yes yep yup yeah yea yeh yah ye yee yus yush yesh yis yiss yas yass yaas yaass yessir yessss aye " +
        "ok okay okie okey oki okii okk okies okiedokie okidoki okeydokey dokie dokey doki kk kay mkay mhm mhmm uhhuh huh " +
        "aight ight aite alr alright roger rgr " +
        // no / disagreement
        "nope nop nah naw nuh nay nawp " +
        // laughter, memes and streaming emotes (repeated forms like hahaha / jajaja / xdddd / kkkk are matched by pattern)
        "lol lul lel lmao lmfao rofl roflmao kek kekw lulw omegalul xd xdd xddd mdr ptdr xptdr kappa pog poggers " +
        "pogchamp copium hopium sadge monkas uwu owo nya rawr xoxo hah heh " +
        // reactions and interjections
        "rip oof ouch oops oopsie whoops welp yikes bruh bro wow woah whoa omg omfg wtf wth ffs damn dang darn dayum " +
        "gosh geez jeez sheesh yay woo woot w00t hype nice noice cool kewl hm hmm uh umm uhm erm eh ah oh ooh aw aww " +
        "ew eww ugh meh psst shh phew whew tsk pff pfft bah boo yum ay ayy uy ups aie " +
        // thanks / congrats / sportsmanship
        "gg ggs ggwp ggez wp gj nt gl hf glhf ez ezpz rekt pwned noob nub n00b ty tyty tyvm tysm thx thnx tnx yw np " +
        "gz gratz grats congrats congratz " +
        // greetings and coming / going (o/ \o o7 are not words; they are filtered out before)
        "o7 hi hii hey heya hiya hai hello howdy sup wassup re wb gn gm nite gnite nini cya bye byee bai " +
        "brb bbl bbiab afk " +
        // MMO / FFXIV shorthand
        "lfg lfm lfp lf1m lf2m lf3m dps tank tanks healer healers heal heals melee caster mt ot aoe tb tbs buff " +
        "debuff nerf lb lb1 lb2 lb3 pf df dc rdy ready inc oom rez gcd ogcd cd cds dot aggro mech mechs " +
        "ff ff14 ffxiv xiv fc ls cwls ex savage ult unreal dungeon dungeons raid raids boss loot mount quest " +
        "roulette trial extreme glam glamour expert msq dd ilvl gil fate fates sprout ooc rp erp " +
        // FFXIV jobs (WAR is left out: German "war")
        "pld drk gnb whm sch ast sge mnk drg nin sam rpr vpr brd mch dnc blm smn rdm pct blu");

    /// <summary>Laughter typed as repeated syllables: haha, ahah, hehe, jajaja, jejeje, kkkk, rsrs, xdddd, looool, ахаха...</summary>
    [GeneratedRegex(@"^(?:a?(?:ha){2,}h?|(?:ah){2,}a?|(?:he){2,}h?|(?:hi){2,}h?|(?:ho){2,}|a?(?:ja){2,}j?|(?:je){2,}j?|(?:ji){2,}|" +
                    @"(?:ke){2,}k?|k{3,}|(?:rs){2,}|x+d+|l+[o0]+l+|lmf?a+o+|w{3,}|а?(?:ха){2,}х?|(?:ах){2,}а?|(?:хе){2,}|(?:хи){2,})$",
                    RegexOptions.CultureInvariant)]
    private static partial Regex LaughterRegex();

    /// <summary>Letters repeated 3+ times ("yesss", "noooope", "riiip").</summary>
    [GeneratedRegex(@"(\p{L})\1{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex LongRunRegex();

    [GeneratedRegex(@"(\p{L})\1{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex LongRun();

    [GeneratedRegex(@"(\p{L})\1+", RegexOptions.CultureInvariant)]
    private static partial Regex AnyRunRegex();

    /// <summary>Doubled final letter ("yess", "hii", "okk"); not inside a word ("nnt" is not "nt").</summary>
    [GeneratedRegex(@"(\p{L})\1+$", RegexOptions.CultureInvariant)]
    private static partial Regex RunRegex();

    // Digits stay inside words ("4to" is Russian что, "b4" English before), but a word needs a letter.
    [GeneratedRegex(@"[\p{L}\p{N}']*\p{L}[\p{L}\p{N}']*", RegexOptions.CultureInvariant)]
    private static partial Regex WordRegex();

    [GeneratedRegex(@"^(?:dell|nell|all|dall|sull|quell|sull|un|po)'|^c'è", RegexOptions.CultureInvariant)]
    private static partial Regex ItalianElision();

    [GeneratedRegex(@"zh|kh|shch|^vs[a-z]|^vz|^zd|[a-z]'y[aeou]|(?:ogo|ego|tsya|sya|yy|iy|aya|uyu|yat|aesh|eesh|iesh|uesh|yaesh|tsa|ayu|yayu|aet|ayet|aem|ayut|yaet)$",
                    RegexOptions.CultureInvariant)]
    private static partial Regex TranslitShapeRegex();

    /// <summary>True when the text reads as Russian (Cyrillic, or translit with a clear margin over other languages).</summary>
    public static bool LooksRussian(string text) => Detect(text, new[] { "RU" }) == "RU";

    private static HashSet<string> Set(string words) =>
        new(words.Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Jargon that carries no language information (gg, rip, yep, dps, tank...), including elongated
    /// spellings ("yesss", "riiip", "okkk") and typed laughter ("hahaha", "jajaja", "xdddd").
    /// </summary>
    public static bool IsNeutralWord(string word)
    {
        if (word.Length == 0)
            return false;
        if (Neutral.Contains(word))
            return true;
        var lower = word.ToLowerInvariant();
        if (LaughterRegex().IsMatch(lower))
            return true;
        // Elongations: "yesss" -> "yess" / "yes", "noooope" -> "noope" / "nope", "hmmmm" -> "hmm".
        var two = LongRunRegex().Replace(lower, "$1$1");
        if (two != lower && Neutral.Contains(two))
            return true;
        // A clearly elongated word ("riiip", "yeeesss") may lose every doubled letter; otherwise only a final one ("yess").
        var one = LongRunRegex().IsMatch(lower) ? AnyRunRegex().Replace(lower, "$1") : RunRegex().Replace(lower, "$1");
        return one != lower && one.Length > 1 && Neutral.Contains(one);
    }

    /// <summary>
    /// True when <paramref name="word"/> counts as evidence for <paramref name="lang"/>. Neutral jargon
    /// ("yes", "gg", "ty", "xD") never does, except as Russian translit (where "ty" is ты).
    /// </summary>
    private static bool Votes(string lang, string word, HashSet<string> stop)
    {
        // Russian translit shares short words with MMO jargon ("ty" is ты): those still count for Russian.
        if (lang == "RU")
            return stop.Contains(word) || (word.Length > 1 && Slang.IsKnown(lang, word)) || Translit.IsLexiconWord(word);
        if (IsNeutralWord(word))
            return false;
        if (stop.Contains(word))
            return true;
        // Chat shorthand of this language ("ya" counts for French and Russian alike: the weights split it).
        return word.Length > 1 && Slang.IsKnown(lang, word);
    }

    /// <summary>True when the text has no real words worth translating ("gg", "o/", emotes, numbers...).</summary>
    public static bool IsTrivial(string text)
    {
        var words = WordRegex().Matches(text).Select(m => m.Value).ToList();
        if (words.Count == 0)
            return true;
        return words.All(w => w.Length == 1 || IsNeutralWord(w.Trim('\'')));
    }

    /// <summary>Guesses which of <paramref name="candidates"/> the text is written in, or null when unsure.</summary>
    /// <param name="minScore">Evidence needed for a verdict: 2 for short chunks of a longer message, so a single
    /// shared word ("te", "si", "so") cannot switch the language of one sentence.</param>
    /// <param name="onlyCandidates">Score the candidates alone (a second look at a short chunk between the user's two
    /// languages): "e" / "o" then count for Portuguese against English instead of being shared with Italian.</param>
    public static string? Detect(string text, IReadOnlyCollection<string> candidates, double minScore = 1, bool onlyCandidates = false)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        // Japanese: kana is unambiguous. Other CJK text (or Japanese outside the candidates) is left to the
        // engine: a stray "ty" or "a" in it must not make it Russian or English.
        if (text.Any(c => c is >= '぀' and <= 'ヿ'))
            return candidates.Contains("JA") ? "JA" : null;
        if (text.Any(c => c is >= '\u3400' and <= '\u9FFF' or >= '\uFF00' and <= '\uFFEF'))
            return null;

        // Cyrillic script: Russian (Ukrainian etc. are left to the engine when Russian is not a candidate).
        if (IsMostlyCyrillic(text))
            return candidates.Contains("RU") ? "RU" : null;

        var words = WordRegex().Matches(text.ToLowerInvariant())
                               .Select(m => m.Value.Trim('\''))
                               .Where(w => w.Length > 0)
                               .ToList();
        if (words.Count == 0)
            return null;

        // Each word splits its vote between the languages it belongs to: "de" / "la" / "en" (FR, ES, PT, IT...)
        // weigh little, "esta" or "nicht" a full point.
        var weights = words.Select(w =>
        {
            var langs = StopWords.Where(kv => (!onlyCandidates || candidates.Contains(kv.Key)) && Votes(kv.Key, w, kv.Value))
                                 .Select(kv => kv.Key).ToList();
            // MMO shorthand that is also Russian translit ("ty") is only half a point: "... junto al fuego ty" is not Russian.
            return (Langs: langs, Weight: langs.Count == 0 ? 0 : (IsNeutralWord(w) ? 0.5 : 1.0) / langs.Count);
        }).ToList();

        var scores = new Dictionary<string, double>();
        // Score every language we know, not only the candidates: a Spanish message must not be
        // mistaken for French just because Spanish is not part of the configured pair.
        foreach (var (lang, _) in StopWords)
        {
            if (onlyCandidates && !candidates.Contains(lang))
                continue;
            var score = weights.Where(x => x.Langs.Contains(lang)).Sum(x => x.Weight);
            // Elided forms (j'ai, c'est, qu'il...) are a strong French signal; l' is shared with Italian (l'evento),
            // which has its own (dell', nell', all', un', c'è) and stressed final vowels (città, più, però).
            if (lang == "FR")
                score += words.Sum(w => w.Length > 2 && w[1] == '\'' && "jcdmnstq".Contains(w[0]) && !w.StartsWith("c'è") ? 1.5
                                      : w.Length > 2 && w.StartsWith("l'") ? 0.5 : 0);
            if (lang == "IT")
                score += words.Sum(w => ItalianElision().IsMatch(w) ? 1.5 : w.StartsWith("l'") ? 0.5 : 0) +
                         0.5 * words.Count(w => w.Length > 2 && "àìòù".Contains(w[^1]) && w is not ("déjà" or "voilà" or "deja" or "voila"));
            // Accents: only the ones a language has to itself count fully (é / à / ê / ç are also Portuguese / Italian).
            if (lang == "FR")
                score += 0.5 * text.Count(c => "ëîïûœ".Contains(char.ToLowerInvariant(c))) +
                         0.25 * text.Count(c => "éèêàâçôù".Contains(char.ToLowerInvariant(c)));
            if (lang == "ES")
                score += 0.5 * text.Count(c => "ñáíóú¿¡".Contains(char.ToLowerInvariant(c)));
            if (lang == "DE")
                score += 0.5 * text.Count(c => "äöüß".Contains(char.ToLowerInvariant(c)));
            if (lang == "PT")
                score += text.Count(c => "ãõ".Contains(char.ToLowerInvariant(c))) +
                         0.25 * text.Count(c => "çêô".Contains(char.ToLowerInvariant(c)));
            // Russian translit has word shapes no other language here has (zh, kh, shch, -ogo, -tsya, -yy...).
            if (lang == "RU")
                score += words.Count(w => TranslitShapeRegex().IsMatch(w) && !LongRun().IsMatch(w)); // not "deliveryyy"
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
        if (best.Value >= minScore && best.Value >= second * 1.5 && best.Value - second >= 0.75)
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
