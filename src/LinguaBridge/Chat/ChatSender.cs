using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using LinguaBridge.Translation;

namespace LinguaBridge.Chat;

/// <summary>Sends a line to the game's chat box exactly as if the user had typed it.</summary>
public static class ChatSender
{
    public const int MaxBytes = 500;

    /// <summary>Must be called on the framework thread.</summary>
    public static unsafe void Send(string message)
    {
        var bytes = Encoding.UTF8.GetByteCount(message);
        if (bytes == 0)
            throw new ArgumentException("Message is empty.");
        if (bytes > MaxBytes)
            throw new ArgumentException($"Message is too long ({bytes}/{MaxBytes} bytes).");
        if (message.IndexOfAny(new[] { '\r', '\n' }) >= 0)
            throw new ArgumentException("Message must be a single line.");

        var str = Utf8String.FromString(message);
        try
        {
            // Strip anything the game would not let a user type by hand.
            str->SanitizeString((AllowedEntities)0x27F);
            if (str->Length == 0)
                throw new ArgumentException("Message is empty after sanitizing.");
            UIModule.Instance()->ProcessChatBoxEntry(str);
        }
        finally
        {
            str->Dtor(true);
        }
    }

    /// <summary>
    /// Replaces characters the game font (AXIS) cannot draw with close equivalents, so text shows
    /// correctly in the chat log. Accents like é è à ç ê are fine; œ, typographic quotes and
    /// French narrow spaces are not.
    /// </summary>
    public static string FoldForGame(string text)
    {
        // The game font has no Cyrillic: write Russian in Latin letters, the way Russian players type in game.
        text = Translit.ToLatin(text);
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            switch (c)
            {
                case '\r' or '\n' or '\t': sb.Append(' '); break;
                case '\u00A0' or '\u202F' or '\u2009' or '\u2007' or '\u200B': sb.Append(' '); break;
                case '\u2018' or '\u2019' or '\u02BC' or '\u2032': sb.Append('\''); break;
                case '\u201C' or '\u201D' or '\u201E' or '\u2033': sb.Append('"'); break;
                case '\u2013' or '\u2014' or '\u2212': sb.Append('-'); break;
                case '\u2026': sb.Append("..."); break;
                case '\u0153': sb.Append("oe"); break;
                case '\u0152': sb.Append("OE"); break;
                case '\u2022': sb.Append('-'); break;
                case '\u20AC': sb.Append("EUR"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Makes engine output safe for the game chat: one line, characters the game font can show,
    /// and never starting with '/' (which would run a command).
    /// </summary>
    public static string Clean(string text)
    {
        var result = FoldForGame(text).Trim();
        while (result.StartsWith('/'))
            result = result[1..].TrimStart();
        return result;
    }

    /// <summary>
    /// Splits <paramref name="body"/> into chat lines that fit in <see cref="MaxBytes"/> together with
    /// <paramref name="prefix"/> (e.g. "/p ") and an optional " (1/3)" marker. Prefers sentence ends,
    /// then spaces; never cuts inside a word unless a single word is longer than a line.
    /// </summary>
    public static List<string> Split(string prefix, string body, bool markers)
    {
        var budget = MaxBytes - Encoding.UTF8.GetByteCount(prefix) - (markers ? Encoding.UTF8.GetByteCount(" (10/10)") : 0);
        if (Encoding.UTF8.GetByteCount(prefix + body) <= MaxBytes)
            return new List<string> { prefix + body };

        var parts = new List<string>();
        var rest = body.Trim();
        while (rest.Length > 0)
        {
            if (Encoding.UTF8.GetByteCount(rest) <= budget)
            {
                parts.Add(rest);
                break;
            }

            // Longest prefix that fits the byte budget.
            var fit = 0;
            var bytes = 0;
            while (fit < rest.Length)
            {
                var b = Encoding.UTF8.GetByteCount(rest.AsSpan(fit, char.IsHighSurrogate(rest[fit]) ? 2 : 1));
                if (bytes + b > budget)
                    break;
                bytes += b;
                fit += char.IsHighSurrogate(rest[fit]) ? 2 : 1;
            }

            int cut = LastBreak(rest, fit, ".!?\u2026\"*)") ?? LastBreak(rest, fit, ",;:") ?? rest.LastIndexOf(' ', Math.Max(0, fit - 1));
            if (cut <= 0 || cut < fit / 3)
                cut = fit;
            parts.Add(rest[..cut].TrimEnd());
            rest = rest[cut..].TrimStart();
        }

        if (!markers || parts.Count == 1)
            return parts.ConvertAll(p => prefix + p);
        return parts.Select((p, i) => $"{prefix}{p} ({i + 1}/{parts.Count})").ToList();
    }

    /// <summary>Index just after the last break character (followed by a space) before <paramref name="limit"/>.</summary>
    private static int? LastBreak(string text, int limit, string chars)
    {
        for (var i = Math.Min(limit, text.Length) - 1; i > 0; i--)
        {
            if (chars.IndexOf(text[i]) >= 0 && (i + 1 >= text.Length || text[i + 1] == ' '))
                return i + 1 <= limit ? i + 1 : null;
        }
        return null;
    }
}
