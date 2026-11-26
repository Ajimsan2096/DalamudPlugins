using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace LinguaBridge.Translation;

/// <summary>Name matching for the player lists in the settings ("Hanna", "Hanna Weber", "Hanna Weber@Odin").</summary>
public static class PlayerNames
{
    /// <summary>
    /// True when <paramref name="entry"/> names <paramref name="sender"/>: the full name, or one whole part of it
    /// ("Hanna" matches "Hanna Weber", but "Han" or "Al" match nobody else by accident). "@World" is ignored.
    /// </summary>
    public static bool Matches(string sender, string entry)
    {
        var e = entry.Trim();
        var at = e.IndexOf('@');
        if (at >= 0)
            e = e[..at].Trim();
        if (e.Length == 0 || string.IsNullOrWhiteSpace(sender))
            return false;
        var s = sender.Trim();
        if (s.Equals(e, StringComparison.OrdinalIgnoreCase))
            return true;
        return !e.Contains(' ') && s.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                                    .Any(part => part.Equals(e, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>True when any entry of <paramref name="list"/> names the sender.</summary>
    public static bool AnyMatch(IEnumerable<string> list, string sender) => list.Any(e => Matches(sender, e));

    /// <summary>True when <paramref name="word"/> appears in <paramref name="text"/> as a whole word.</summary>
    public static bool ContainsWord(string text, string word) =>
        word.Length > 0 && text.Length > 0 &&
        Regex.IsMatch(text, @"(?<![\p{L}\p{N}])" + Regex.Escape(word) + @"(?![\p{L}\p{N}])",
                      RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}
