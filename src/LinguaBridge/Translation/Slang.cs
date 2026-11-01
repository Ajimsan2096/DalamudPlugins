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
        }),
        ["ES"] = Map(new()
        {
            ["q"] = "que", ["xq"] = "porque", ["pq"] = "porque", ["tb"] = "también", ["tmb"] = "también",
            ["nse"] = "no sé", ["ns"] = "no sé", ["porfa"] = "por favor", ["xfa"] = "por favor", ["dnd"] = "dónde",
            ["bn"] = "bien", ["mñn"] = "mañana", ["salu2"] = "saludos", ["tqm"] = "te quiero mucho"
        }),
    };

    [GeneratedRegex(@"(?<![\p{L}\p{N}'/-])[\p{L}\p{N}/-]+(?![\p{L}\p{N}'])", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();

    private static Dictionary<string, string> Map(Dictionary<string, string> d) => new(d, StringComparer.OrdinalIgnoreCase);

    public static bool IsKnown(string language, string word) =>
        Maps.TryGetValue(language, out var map) && map.ContainsKey(word);

    /// <summary>Replaces known abbreviations of <paramref name="language"/> in <paramref name="text"/>.</summary>
    public static string Expand(string text, string language)
    {
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
