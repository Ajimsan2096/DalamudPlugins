using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Text;

namespace LinguaBridge.Chat;

/// <summary>
/// Remembers the last lines of each conversation (party, FC, a tell partner, say/emotes...) so they can be
/// sent to DeepL as context. Context is not billed by DeepL and helps with pronouns, gender, tu/vous and tone.
/// </summary>
public sealed class ConversationLog
{
    private const int MaxLines = 12;
    private const int MaxContextChars = 1500;
    private readonly Dictionary<string, LinkedList<string>> logs = new();
    private readonly object gate = new();

    /// <summary>Key of the conversation a message belongs to. Party and cross-world party are the same chat.</summary>
    public static string KeyFor(XivChatType type, string? tellPartner) => type switch
    {
        XivChatType.TellIncoming or XivChatType.TellOutgoing => "tell:" + (tellPartner ?? "?"),
        XivChatType.CrossParty => "ch:" + XivChatType.Party,
        XivChatType.CustomEmote or XivChatType.StandardEmote => "ch:" + XivChatType.Say, // RP happens in say + emotes
        _ => "ch:" + type,
    };

    public void Add(string key, string sender, string text, bool emote)
    {
        var clean = new string(text.Where(c => c is < '' or > '').ToArray()).Trim();
        if (clean.Length == 0)
            return;
        var line = emote ? $"{sender} {clean}" : $"{sender}: {clean}";
        lock (gate)
        {
            if (!logs.TryGetValue(key, out var log))
                logs[key] = log = new LinkedList<string>();
            log.AddLast(line);
            while (log.Count > MaxLines)
                log.RemoveFirst();
        }
    }

    /// <summary>The last <paramref name="lines"/> lines (oldest first), or null when there are none.</summary>
    public string? Recent(string key, int lines)
    {
        if (lines <= 0)
            return null;
        lock (gate)
        {
            if (!logs.TryGetValue(key, out var log) || log.Count == 0)
                return null;
            var picked = log.Skip(Math.Max(0, log.Count - lines)).ToList();
            var text = string.Join("\n", picked);
            return text.Length <= MaxContextChars ? text : text[^MaxContextChars..];
        }
    }
}
