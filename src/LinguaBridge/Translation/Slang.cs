using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace LinguaBridge.Translation;

/// <summary>
/// Expands chat/SMS abbreviations ("jsp", "tkt", "idk"...) into full words before translation.
/// Engines often leave these untouched or misread the whole sentence because of them.
/// Game jargon (dps, tank, afk, brb...) is intentionally NOT expanded.
/// </summary>
public static partial class Slang
{
    private static readonly Dictionary<string, Dictionary<string, string>> Maps = new()
    {
        ["FR"] = Map(new()
        {
            ["jsp"] = "je sais pas", ["jsais"] = "je sais", ["jpp"] = "j'en peux plus", ["tkt"] = "t'inquiète",
            ["tqt"] = "t'inquiète", ["slt"] = "salut", ["bjr"] = "bonjour", ["bsr"] = "bonsoir", ["cc"] = "coucou",
            ["pk"] = "pourquoi", ["pq"] = "pourquoi", ["pcq"] = "parce que", ["pck"] = "parce que", ["psk"] = "parce que",
            ["stp"] = "s'il te plaît", ["svp"] = "s'il vous plaît", ["dsl"] = "désolé", ["mtn"] = "maintenant",
            ["mnt"] = "maintenant", ["tjr"] = "toujours", ["tjrs"] = "toujours", ["bcp"] = "beaucoup",
            ["qqn"] = "quelqu'un", ["qqch"] = "quelque chose", ["qq"] = "quelques", ["jtm"] = "je t'aime",
            ["osef"] = "on s'en fout", ["jvais"] = "je vais", ["jveux"] = "je veux", ["chui"] = "je suis",
            ["chuis"] = "je suis", ["jsuis"] = "je suis", ["jpense"] = "je pense", ["jcrois"] = "je crois",
            ["jme"] = "je me", ["jte"] = "je te", ["jle"] = "je le", ["jai"] = "j'ai", ["ct"] = "c'était",
            ["vrm"] = "vraiment", ["vrmt"] = "vraiment", ["rdv"] = "rendez-vous", ["auj"] = "aujourd'hui",
            ["ajd"] = "aujourd'hui", ["dac"] = "d'accord", ["dacc"] = "d'accord", ["nn"] = "non", ["ouai"] = "ouais",
            ["pr"] = "pour", ["ds"] = "dans", ["tt"] = "tout", ["tlm"] = "tout le monde", ["frr"] = "frère",
            ["oklm"] = "tranquille", ["askip"] = "à ce qu'il paraît", ["jdois"] = "je dois", ["jpeux"] = "je peux",
            ["ya"] = "il y a", ["yen"] = "il y en", ["ptet"] = "peut-être", ["p-e"] = "peut-être", ["mm"] = "même",
            ["kan"] = "quand", ["koi"] = "quoi", ["ki"] = "qui", ["bb"] = "bébé", ["bn"] = "bonne nuit",
            ["ca"] = "ça", ["deja"] = "déjà", ["tres"] = "très", ["etre"] = "être", ["apres"] = "après",
            ["voila"] = "voilà", ["forcant"] = "forçant", ["commencant"] = "commençant", ["francais"] = "français",
            ["francaise"] = "française", ["garcon"] = "garçon", ["recu"] = "reçu", ["calin"] = "câlin", ["calins"] = "câlins",
            ["caline"] = "câline", ["calines"] = "câlines", ["caliner"] = "câliner", ["fache"] = "fâché", ["fachee"] = "fâchée",
            ["cv"] = "ça va", ["stv"] = "si tu veux", ["jtd"] = "je t'adore", ["bsx"] = "bisous", ["biz"] = "bises",
            ["pti"] = "petit", ["ptite"] = "petite", ["tjs"] = "toujours", ["fds"] = "fin de semaine", ["dmn"] = "demain",
        }),
        ["EN"] = Map(new()
        {
            ["idk"] = "I don't know", ["imo"] = "in my opinion", ["imho"] = "in my humble opinion",
            ["tbh"] = "to be honest", ["ngl"] = "not gonna lie", ["nvm"] = "never mind", ["omw"] = "on my way",
            ["rn"] = "right now", ["irl"] = "in real life", ["ofc"] = "of course", ["pls"] = "please",
            ["plz"] = "please", ["wdym"] = "what do you mean", ["ikr"] = "I know, right", ["lmk"] = "let me know",
            ["hmu"] = "hit me up", ["gtg"] = "got to go", ["g2g"] = "got to go", ["cya"] = "see you",
            ["tho"] = "though", ["idc"] = "I don't care", ["iirc"] = "if I remember correctly",
            ["afaik"] = "as far as I know", ["btw"] = "by the way", ["fyi"] = "for your information",
            ["u"] = "you", ["ur"] = "your", ["r"] = "are", ["y"] = "why", ["bc"] = "because", ["cuz"] = "because",
            ["wanna"] = "want to", ["gonna"] = "going to", ["gotta"] = "got to", ["dunno"] = "don't know",
            ["prob"] = "probably", ["probs"] = "probably", ["def"] = "definitely", ["w/e"] = "whatever",
            ["smth"] = "something", ["sth"] = "something", ["sb"] = "somebody", ["abt"] = "about",
            ["ppl"] = "people", ["msg"] = "message", ["tmrw"] = "tomorrow", ["tmr"] = "tomorrow", ["2day"] = "today",
            ["2nite"] = "tonight", ["b4"] = "before", ["l8r"] = "later", ["ty"] = "thank you", ["thx"] = "thanks",
            ["np"] = "no problem", ["yw"] = "you're welcome", ["jk"] = "just kidding", ["srsly"] = "seriously",
            ["ily"] = "I love you", ["ilysm"] = "I love you so much", ["ttyl"] = "talk to you later",
            ["wyd"] = "what are you doing", ["hbu"] = "how about you", ["wbu"] = "what about you", ["ik"] = "I know",
            ["istg"] = "I swear to God", ["tysm"] = "thank you so much", ["smh"] = "shaking my head",
            ["idrc"] = "I don't really care", ["kinda"] = "kind of", ["sorta"] = "sort of", ["lemme"] = "let me",
            ["gimme"] = "give me", ["imma"] = "I'm going to", ["dw"] = "don't worry", ["bday"] = "birthday",
            ["luv"] = "love", ["tbf"] = "to be fair", ["afaict"] = "as far as I can tell",
        }),
        ["DE"] = Map(new()
        {
            ["hdl"] = "hab dich lieb", ["hdgdl"] = "hab dich ganz doll lieb", ["hdal"] = "hab dich auch lieb",
            ["ild"] = "ich liebe dich", ["ka"] = "keine Ahnung", ["kp"] = "kein Plan", ["vllt"] = "vielleicht",
            ["vlt"] = "vielleicht", ["vlcht"] = "vielleicht", ["evtl"] = "eventuell", ["mmn"] = "meiner Meinung nach",
            ["lg"] = "liebe Grüße", ["glg"] = "ganz liebe Grüße", ["gn8"] = "gute Nacht", ["n8"] = "Nacht",
            ["bb"] = "bis bald", ["bd"] = "bis dann", ["sry"] = "sorry", ["zb"] = "zum Beispiel", ["dh"] = "das heißt",
            ["bzw"] = "beziehungsweise", ["ggf"] = "gegebenenfalls", ["iwie"] = "irgendwie", ["iwas"] = "irgendwas",
            ["iwo"] = "irgendwo", ["iwann"] = "irgendwann", ["iwer"] = "irgendwer", ["jmd"] = "jemand", ["nich"] = "nicht",
            ["jz"] = "jetzt", ["jetz"] = "jetzt", ["nochma"] = "nochmal", ["nen"] = "einen", ["ner"] = "einer",
            ["nem"] = "einem", ["u"] = "und", ["nd"] = "und", ["wg"] = "wegen", ["haste"] = "hast du",
            ["biste"] = "bist du", ["kannste"] = "kannst du", ["willste"] = "willst du", ["weißte"] = "weißt du",
            ["weisste"] = "weißt du", ["machste"] = "machst du", ["siehste"] = "siehst du", ["gehste"] = "gehst du",
            ["gibts"] = "gibt es", ["gehts"] = "geht es", ["isses"] = "ist es", ["glaub"] = "glaube",
            ["hdf"] = "halt die Fresse", ["thx"] = "danke", ["dnk"] = "danke", ["np"] = "kein Problem",
            // common words typed without umlauts / ß (unambiguous ones only)
            ["fuer"] = "für", ["ueber"] = "über", ["schoen"] = "schön", ["schoene"] = "schöne", ["schoenen"] = "schönen",
            ["moechte"] = "möchte", ["moechtest"] = "möchtest", ["koennen"] = "können", ["konnen"] = "können",
            ["koennte"] = "könnte", ["muessen"] = "müssen", ["mussen"] = "müssen", ["muesste"] = "müsste",
            ["waere"] = "wäre", ["haette"] = "hätte", ["wuerde"] = "würde", ["wuerdest"] = "würdest",
            ["natuerlich"] = "natürlich", ["naturlich"] = "natürlich", ["frueh"] = "früh", ["spaet"] = "spät",
            ["spaeter"] = "später", ["spater"] = "später", ["suess"] = "süß", ["suesse"] = "süße", ["suesser"] = "süßer",
            ["suss"] = "süß", ["susse"] = "süße", ["maedchen"] = "Mädchen", ["madchen"] = "Mädchen",
            ["gruesse"] = "Grüße", ["tschuess"] = "tschüss", ["tschuss"] = "tschüss", ["uebrigens"] = "übrigens",
            ["ubrigens"] = "übrigens", ["ueberhaupt"] = "überhaupt", ["uberhaupt"] = "überhaupt",
            ["gefuehl"] = "Gefühl", ["gefuehle"] = "Gefühle", ["fuehle"] = "fühle", ["fuehlst"] = "fühlst",
            ["kuessen"] = "küssen", ["kuesse"] = "Küsse", ["haende"] = "Hände", ["waehrend"] = "während",
            ["zurueck"] = "zurück", ["zuruck"] = "zurück", ["naechste"] = "nächste", ["naechsten"] = "nächsten",
            ["moeglich"] = "möglich", ["moglich"] = "möglich", ["gluecklich"] = "glücklich", ["glucklich"] = "glücklich",
            ["glueck"] = "Glück", ["gemuetlich"] = "gemütlich", ["haesslich"] = "hässlich", ["aerger"] = "Ärger",
            ["heisst"] = "heißt", ["weiss"] = "weiß", ["gross"] = "groß", ["grosse"] = "große", ["strasse"] = "Straße",
            ["muede"] = "müde", ["hoeren"] = "hören", ["hoer"] = "hör", ["oefter"] = "öfter", ["koenig"] = "König",
            ["koenigin"] = "Königin", ["spass"] = "Spaß", ["schaetzchen"] = "Schätzchen",
        }),
        ["ES"] = Map(new()
        {
            ["q"] = "que", ["xq"] = "porque", ["pq"] = "porque", ["tb"] = "también", ["tmb"] = "también",
            ["nse"] = "no sé", ["ns"] = "no sé", ["porfa"] = "por favor", ["xfa"] = "por favor", ["dnd"] = "dónde",
            ["bn"] = "bien", ["mñn"] = "mañana", ["salu2"] = "saludos", ["tqm"] = "te quiero mucho",
            ["k"] = "que", ["x"] = "por", ["xa"] = "para", ["pa"] = "para", ["toy"] = "estoy", ["tas"] = "estás",
            ["tmbn"] = "también", ["msj"] = "mensaje", ["finde"] = "fin de semana", ["xd"] = "xD",
            // common words typed without accents (unambiguous ones only)
            ["muchisimo"] = "muchísimo", ["muchisima"] = "muchísima", ["tambien"] = "también", ["aqui"] = "aquí",
            ["asi"] = "así", ["despues"] = "después", ["dia"] = "día", ["dias"] = "días", ["corazon"] = "corazón",
            ["jamas"] = "jamás", ["ademas"] = "además", ["algun"] = "algún", ["ningun"] = "ningún", ["facil"] = "fácil",
            ["dificil"] = "difícil", ["rapido"] = "rápido", ["musica"] = "música", ["razon"] = "razón",
            ["gustaria"] = "gustaría", ["podria"] = "podría", ["deberia"] = "debería", ["querria"] = "querría",
            ["tendria"] = "tendría", ["haria"] = "haría", ["diria"] = "diría", ["estaria"] = "estaría",
            ["habia"] = "había", ["sabia"] = "sabía", ["queria"] = "quería", ["tenia"] = "tenía", ["podias"] = "podías",
            ["carino"] = "cariño", ["anos"] = "años", ["manana"] = "mañana", ["pequeno"] = "pequeño", ["pequena"] = "pequeña",
            ["perdon"] = "perdón", ["cancion"] = "canción",
            // more chat abbreviations
            ["ntp"] = "no te preocupes", ["tkm"] = "te quiero mucho", ["tqmm"] = "te quiero muchísimo", ["bss"] = "besos",
            ["bsos"] = "besos", ["grax"] = "gracias", ["grcs"] = "gracias", ["npi"] = "ni puta idea", ["kiero"] = "quiero",
            ["kieres"] = "quieres", ["aki"] = "aquí", ["ke"] = "que", ["xo"] = "pero", ["weno"] = "bueno", ["bno"] = "bueno",
            ["vdd"] = "verdad", ["sk"] = "es que", ["esk"] = "es que", ["toi"] = "estoy", ["tamb"] = "también",
            ["dsp"] = "después", ["kpasa"] = "qué pasa", ["qtal"] = "qué tal", ["xfavor"] = "por favor",
            ["dnada"] = "de nada", ["nps"] = "no pasa nada",
            ["ahi"] = "ahí", ["alli"] = "allí", ["tio"] = "tío", ["tia"] = "tía", ["todavia"] = "todavía",
            ["segun"] = "según", ["quizas"] = "quizás", ["detras"] = "detrás", ["atras"] = "atrás", ["boton"] = "botón",
            ["mision"] = "misión", ["pelicula"] = "película", ["telefono"] = "teléfono", ["aleman"] = "alemán",
            ["ingles"] = "inglés", ["frances"] = "francés", ["espanol"] = "español",
            ["espanola"] = "española", ["senor"] = "señor", ["senora"] = "señora", ["nino"] = "niño", ["companero"] = "compañero",
            ["companera"] = "compañera", ["sonrio"] = "sonrío", ["iria"] = "iría", ["vendria"] = "vendría",
            ["sabria"] = "sabría", ["pondria"] = "pondría", ["practicamente"] = "prácticamente",
            ["ultimamente"] = "últimamente", ["timido"] = "tímido", ["timida"] = "tímida", ["simpatico"] = "simpático",
            ["simpatica"] = "simpática", ["guapisima"] = "guapísima", ["guapisimo"] = "guapísimo", ["bonisima"] = "bonísima",
        }),
        // Russian chat shorthand, in Cyrillic (translit is converted to Cyrillic before this runs: "sps" -> "спс").
        ["RU"] = Map(new()
        {
            ["спс"] = "спасибо", ["пж"] = "пожалуйста", ["плз"] = "пожалуйста", ["пжлст"] = "пожалуйста",
            ["мб"] = "может быть", ["хз"] = "не знаю", ["щас"] = "сейчас", ["ща"] = "сейчас", ["счас"] = "сейчас",
            ["норм"] = "нормально", ["оч"] = "очень", ["прив"] = "привет", ["кст"] = "кстати", ["чё"] = "что",
            ["че"] = "что", ["шо"] = "что", ["тя"] = "тебя", ["мя"] = "меня", ["ваще"] = "вообще", ["нзч"] = "не за что",
            ["пок"] = "пока", ["сорян"] = "извини", ["сори"] = "извини", ["споки"] = "спокойной ночи",
            ["спок"] = "спокойной ночи", ["дс"] = "до свидания", ["тк"] = "так как", ["тож"] = "тоже",
            ["ниче"] = "ничего", ["чет"] = "что-то", ["чтоли"] = "что ли", ["седня"] = "сегодня", ["сёдня"] = "сегодня",
            ["грю"] = "говорю", ["лан"] = "ладно", ["оке"] = "окей", ["ок"] = "окей", ["пасиб"] = "спасибо",
            ["пасиба"] = "спасибо", ["спасиб"] = "спасибо", ["драсте"] = "здравствуйте", ["здрасте"] = "здравствуйте",
            ["го"] = "давай", ["гоу"] = "давай", ["изи"] = "легко", ["имхо"] = "по-моему", ["др"] = "день рождения",
            ["чел"] = "человек", ["пон"] = "понятно",
            ["оки"] = "окей", ["плиз"] = "пожалуйста", ["пасибки"] = "спасибо", ["чмоки"] = "целую", ["чмок"] = "целую",
            ["бро"] = "братан", ["кмк"] = "как мне кажется", ["хд"] = "ха-ха",
        }),
    };

    [GeneratedRegex(@"(?<![\p{L}\p{N}'/-])[\p{L}\p{N}/-]+(?![\p{L}\p{N}'])", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();

    private static Dictionary<string, string> Map(Dictionary<string, string> d) => new(d, StringComparer.OrdinalIgnoreCase);

    public static bool IsKnown(string language, string word) =>
        Maps.TryGetValue(language, out var map) && map.ContainsKey(word);

    /// <summary>Replaces known abbreviations of <paramref name="language"/> in <paramref name="text"/>.</summary>
    // French: "tu a" / "tu m'a" / "tu n'a" (very common typo for "tu as" / "tu m'as" / "tu n'as").
    [GeneratedRegex(@"\b([Tt]u (?:n'|m'|t'|l')?)a\b(?!['\-])", RegexOptions.CultureInvariant)]
    private static partial Regex FrenchTuARegex();

    public static string Expand(string text, string language)
    {
        if (language == "FR")
            text = FrenchTuARegex().Replace(text, "$1as");
        if (!Maps.TryGetValue(language, out var map))
            return text;
        return TokenRegex().Replace(text, m =>
        {
            if (!map.TryGetValue(m.Value, out var full))
                return m.Value;
            // Keep a leading capital ("Jsp" -> "Je sais pas"), but never upper-case "I"-style expansions.
            return char.IsUpper(m.Value[0]) && full.Length > 0 && char.IsLower(full[0])
                ? char.ToUpperInvariant(full[0]) + full[1..]
                : full;
        });
    }
}
