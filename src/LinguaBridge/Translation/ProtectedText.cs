using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace LinguaBridge.Translation;

/// <summary>
/// Text that may contain "protected" spans (item links, auto-translate phrases, names, user terms)
/// which must reach the reader unchanged. Spans are delimited by two control characters that never
/// appear in game chat; DeepL receives them as &lt;x&gt; tags it is told to ignore.
/// </summary>
public static partial class ProtectedText
{
    public const char Open = '\u0002';
    public const char Close = '\u0003';

    public static string Wrap(string s) => s.Length == 0 ? s : $"{Open}{s}{Close}";

    /// <summary>Text with markers removed (what a human sees).</summary>
    public static string Plain(string text) => text.Replace(Open.ToString(), "").Replace(Close.ToString(), "");

    /// <summary>Text with protected spans removed entirely (for language detection).</summary>
    public static string WithoutProtected(string text) => SpanRegex().Replace(text, " ");

    public static bool HasProtected(string text) => text.IndexOf(Open) >= 0;

    /// <summary>Applies <paramref name="map"/> only to the unprotected parts.</summary>
    public static string MapUnprotected(string text, Func<string, string> map)
    {
        if (!HasProtected(text))
            return map(text);
        var sb = new StringBuilder();
        var last = 0;
        foreach (Match m in SpanRegex().Matches(text))
        {
            sb.Append(map(text[last..m.Index]));
            sb.Append(m.Value);
            last = m.Index + m.Length;
        }
        sb.Append(map(text[last..]));
        return sb.ToString();
    }

    /// <summary>Wraps whole-word occurrences of <paramref name="terms"/> (outside existing spans).</summary>
    public static string ProtectTerms(string text, IEnumerable<string> terms, bool ignoreCase = true)
    {
        var list = terms.Select(t => t.Trim()).Where(t => t.Length > 0).OrderByDescending(t => t.Length).ToList();
        if (list.Count == 0)
            return text;
        var pattern = @"(?<![\p{L}\p{N}])(" + string.Join("|", list.Select(Regex.Escape)) + @")(?![\p{L}\p{N}])";
        var regex = new Regex(pattern, (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None) | RegexOptions.CultureInvariant);
        return MapUnprotected(text, part => regex.Replace(part, m => Wrap(m.Value)));
    }

    /// <summary>
    /// For engines without tag support (Google): each protected span becomes a "{0}", "{1}"... placeholder,
    /// which engines leave alone, so item links and names are not translated ("Espada Hydatos Aumentada").
    /// </summary>
    public static string ToPlaceholders(string text, out List<string> spans)
    {
        var found = new List<string>();
        var result = SpanRegex().Replace(text, m =>
        {
            found.Add(m.Value[1..^1]);
            return "{" + (found.Count - 1) + "}";
        });
        spans = found;
        return result;
    }

    /// <summary>Puts the spans back; null when the engine lost, repeated or garbled a placeholder.</summary>
    public static string? FromPlaceholders(string translated, IReadOnlyList<string> spans)
    {
        var seen = new int[spans.Count];
        var ok = true;
        var result = PlaceholderRegex().Replace(translated, m =>
        {
            var i = int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            if (i >= spans.Count)
            {
                ok = false;
                return m.Value;
            }
            seen[i]++;
            return spans[i];
        });
        return ok && seen.All(n => n == 1) ? result : null;
    }

    public static string ToDeepLXml(string text)
    {
        var escaped = WebUtility.HtmlEncode(text); // escapes & < > " '
        return escaped.Replace(Open.ToString(), "<x>").Replace(Close.ToString(), "</x>");
    }

    public static string FromDeepLXml(string text) =>
        WebUtility.HtmlDecode(text.Replace("<x>", "").Replace("</x>", ""));

    // Full-width braces too: Google often returns "｛0｝" in Japanese.
    [GeneratedRegex(@"[{｛]\s*(\d{1,2})\s*[}｝]", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderRegex();

    /// <summary>
    /// True when the unprotected text already contains something that looks like a "{0}" placeholder (a player
    /// typed it): placeholders would then be ambiguous.
    /// </summary>
    public static bool HasPlaceholderLookalike(string text) => PlaceholderRegex().IsMatch(WithoutProtected(text));

    /// <summary>Game text commands ("&lt;t&gt;", "&lt;flag&gt;", "&lt;se.3&gt;", "&lt;wait.2&gt;"): never translated.</summary>
    [GeneratedRegex(@"<[A-Za-z][A-Za-z0-9]*(?:\.[0-9]+)?>", RegexOptions.CultureInvariant)]
    private static partial Regex GameMacroRegex();

    /// <summary>Wraps game text commands (&lt;t&gt;, &lt;flag&gt;, &lt;pos&gt;, &lt;se.1&gt;...) so engines leave them alone.</summary>
    public static string ProtectGameMacros(string text) =>
        text.IndexOf('<') < 0 ? text : MapUnprotected(text, part => GameMacroRegex().Replace(part, m => Wrap(m.Value)));

    [GeneratedRegex("\u0002[^\u0003]*\u0003", RegexOptions.CultureInvariant)]
    private static partial Regex SpanRegex();
}
