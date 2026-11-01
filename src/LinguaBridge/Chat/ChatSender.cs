using System;
using System.Text;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;

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
}
