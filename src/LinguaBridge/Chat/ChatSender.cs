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
    /// Makes engine output safe for the game chat: one line, characters the game font can show,
    /// and never starting with '/' (which would run a command).
    /// </summary>
    public static string Clean(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            sb.Append(c switch
            {
                '\r' or '\n' or '\t' => ' ',
                ' ' or ' ' or ' ' or ' ' => ' ', // French typographic spaces
                '‘' or '’' or 'ʼ' => '\'',
                '“' or '”' or '„' => '"',
                '–' or '—' => '-',
                '…' => '.',
                _ => c,
            });
            if (c == '…')
                sb.Append("..");
        }

        var result = sb.ToString().Trim();
        while (result.StartsWith('/'))
            result = result[1..].TrimStart();
        return result;
    }
}
