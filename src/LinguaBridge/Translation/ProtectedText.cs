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
    public static string ProtectTerms(string text, IEnumerable<string> terms)
    {
        var list = terms.Select(t => t.Trim()).Where(t => t.Length > 0).OrderByDescending(t => t.Length).ToList();
        if (list.Count == 0)
            return text;
        var pattern = @"(?<![\p{L}\p{N}])(" + string.Join("|", list.Select(Regex.Escape)) + @")(?![\p{L}\p{N}])";
        var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return MapUnprotected(text, part => regex.Replace(part, m => Wrap(m.Value)));
    }

    public static string ToDeepLXml(string text)
    {
        var escaped = WebUtility.HtmlEncode(text); // escapes & < > " '
        return escaped.Replace(Open.ToString(), "<x>").Replace(Close.ToString(), "</x>");
    }

    public static string FromDeepLXml(string text) =>
        WebUtility.HtmlDecode(text.Replace("<x>", "").Replace("</x>", ""));

    [GeneratedRegex("\u0002[^\u0003]*\u0003", RegexOptions.CultureInvariant)]
    private static partial Regex SpanRegex();
}
