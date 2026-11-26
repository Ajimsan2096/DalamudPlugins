using System.Net;
using System.Text.RegularExpressions;

namespace LinguaBridge.Translation;

/// <summary>
/// Keeps roleplay formatting through DeepL by turning it into XML tags DeepL preserves:
///   *action*  -> &lt;a&gt;   "speech" / “speech” / «speech» -> &lt;q&gt;   ((OOC)) / [OOC ...] -> &lt;o&gt;
/// Protected spans (names, links) become &lt;x&gt;, which DeepL is told not to translate.
/// The text inside every tag is still translated; only the delimiters are guaranteed to survive.
/// </summary>
public static partial class RpMarkup
{
    // Sentinels (control chars never present in chat) used between parsing and XML escaping.
    private const char ActOpen = '\u0004', ActClose = '\u0005';
    private const char SayOpen = '\u0006', SayClose = '\u0007';
    private const char OocOpen = '\u000E', OocClose = '\u000F';
    private const char BracketOpen = '\u0010', BracketClose = '\u0011';

    // "OOC" may already be wrapped as a protected term (\u0002OOC\u0003).
    [GeneratedRegex(@"\(\(\s*(.+?)\s*\)\)|\[\s*(\u0002?OOC\u0003?(?![A-Za-z]).*?)\]", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex OocRegex();

    [GeneratedRegex(@"\*{1,2}([^*]+?)\*{1,2}", RegexOptions.CultureInvariant)]
    private static partial Regex ActRegex();

    [GeneratedRegex("\"([^\"]+?)\"|“([^”]+?)”|«\\s*([^»]+?)\\s*»", RegexOptions.CultureInvariant)]
    private static partial Regex SayRegex();

    /// <summary>True when the text uses any roleplay markup worth encoding.</summary>
    public static bool HasMarkup(string text) =>
        OocRegex().IsMatch(text) || ActRegex().IsMatch(text) || SayRegex().IsMatch(text);

    /// <summary>Raw text (with <see cref="ProtectedText"/> markers) to DeepL XML.</summary>
    public static string ToXml(string text, bool roleplay)
    {
        if (roleplay)
        {
            text = OocRegex().Replace(text, m => m.Groups[1].Success
                ? $"{OocOpen}{m.Groups[1].Value}{OocClose}"
                : $"{BracketOpen}{m.Groups[2].Value}{BracketClose}");
            text = ActRegex().Replace(text, m => $"{ActOpen}{m.Groups[1].Value}{ActClose}");
            text = SayRegex().Replace(text, m =>
                $"{SayOpen}{(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value)}{SayClose}");
        }

        var xml = WebUtility.HtmlEncode(text);
        return xml.Replace(ProtectedText.Open.ToString(), "<x>").Replace(ProtectedText.Close.ToString(), "</x>")
                  .Replace(ActOpen.ToString(), "<a>").Replace(ActClose.ToString(), "</a>")
                  .Replace(SayOpen.ToString(), "<q>").Replace(SayClose.ToString(), "</q>")
                  .Replace(OocOpen.ToString(), "<o>").Replace(OocClose.ToString(), "</o>")
                  .Replace(BracketOpen.ToString(), "<b>").Replace(BracketClose.ToString(), "</b>");
    }

    /// <summary>DeepL XML back to chat text. Speech uses straight quotes (the game font has no « » spacing issues).</summary>
    public static string FromXml(string xml)
    {
        var text = xml.Replace("<x>", "").Replace("</x>", "")
                      .Replace("<a>", "*").Replace("</a>", "*")
                      .Replace("<q>", "\"").Replace("</q>", "\"")
                      .Replace("<o>", "((").Replace("</o>", "))")
                      .Replace("<b>", "[").Replace("</b>", "]");
        return WebUtility.HtmlDecode(text);
    }

    /// <summary>Whether a request needs XML handling at all.</summary>
    public static bool NeedsXml(string text, bool roleplay) =>
        ProtectedText.HasProtected(text) || (roleplay && HasMarkup(text));
}
