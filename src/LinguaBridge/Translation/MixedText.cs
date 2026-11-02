using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace LinguaBridge.Translation;

/// <summary>
/// Splits a message into chunks by language, for messages that mix languages, e.g.
/// "Spanish speech... *English action* Spanish speech...". Chunk boundaries are RP markup
/// (*action*, "speech") and sentence ends; each chunk gets its own detected language.
/// </summary>
public static partial class MixedText
{
    public sealed record Chunk(string Text, string Trailing, string? Lang);

    [GeneratedRegex(@"(?<=[.!?…])\s+", RegexOptions.CultureInvariant)]
    private static partial Regex SentenceBreak();

    private static readonly string[] AllCodes = Languages.All.Select(l => l.Code).ToArray();

    /// <summary>Language chunks (adjacent chunks in the same language merged). Unknown chunks inherit a neighbour's language.</summary>
    public static List<Chunk> Split(string text)
    {
        var pieces = new List<(string Text, string Trailing)>();
        foreach (var token in Tokens(text))
        {
            var core = token.TrimEnd();
            if (core.Length == 0)
            {
                if (pieces.Count > 0)
                    pieces[^1] = (pieces[^1].Text, pieces[^1].Trailing + token);
                continue;
            }
            pieces.Add((core, token[core.Length..]));
        }

        var langs = pieces.Select(p =>
        {
            var plain = ProtectedText.WithoutProtected(p.Text);
            return LanguageDetector.Detect(plain, AllCodes) ?? LanguageDetector.WeakGuess(plain, AllCodes);
        }).ToList();
        // Short or ambiguous chunks ("Estoy...", "Ys,") take the language of the chunk before (or after) them.
        for (var i = 1; i < langs.Count; i++)
            langs[i] ??= langs[i - 1];
        for (var i = langs.Count - 2; i >= 0; i--)
            langs[i] ??= langs[i + 1];

        var chunks = new List<Chunk>();
        for (var i = 0; i < pieces.Count; i++)
        {
            if (chunks.Count > 0 && chunks[^1].Lang == langs[i])
            {
                var last = chunks[^1];
                chunks[^1] = last with { Text = last.Text + last.Trailing + pieces[i].Text, Trailing = pieces[i].Trailing };
            }
            else
            {
                chunks.Add(new Chunk(pieces[i].Text, pieces[i].Trailing, langs[i]));
            }
        }
        return chunks;
    }

    /// <summary>True when at least two different languages were detected.</summary>
    public static bool IsMixed(IReadOnlyList<Chunk> chunks) =>
        chunks.Select(c => c.Lang).Where(l => l != null).Distinct().Count() >= 2;

    /// <summary>The language with the most text.</summary>
    public static string? Dominant(IReadOnlyList<Chunk> chunks) =>
        chunks.Where(c => c.Lang != null).GroupBy(c => c.Lang).OrderByDescending(g => g.Sum(c => c.Text.Length))
              .Select(g => g.Key).FirstOrDefault();

    /// <summary>*action* and "speech" spans as their own tokens; other text split into sentences. Whitespace kept.</summary>
    private static IEnumerable<string> Tokens(string text)
    {
        var plain = new StringBuilder();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            var close = c is '*' or '"' ? text.IndexOf(c, i + 1) : -1;
            if (close > i)
            {
                foreach (var s in Sentences(plain.ToString()))
                    yield return s;
                plain.Clear();
                var end = close + 1;
                while (end < text.Length && char.IsWhiteSpace(text[end]))
                    end++;
                yield return text[i..end];
                i = end;
                continue;
            }
            plain.Append(c);
            i++;
        }
        foreach (var s in Sentences(plain.ToString()))
            yield return s;
    }

    private static IEnumerable<string> Sentences(string plain)
    {
        if (plain.Length == 0)
            yield break;
        var last = 0;
        foreach (Match m in SentenceBreak().Matches(plain))
        {
            yield return plain[last..(m.Index + m.Length)];
            last = m.Index + m.Length;
        }
        if (last < plain.Length)
            yield return plain[last..];
    }
}
