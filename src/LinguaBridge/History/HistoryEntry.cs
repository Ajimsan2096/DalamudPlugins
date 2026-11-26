using System;
using Dalamud.Game.Text;
using LinguaBridge.Translation;

namespace LinguaBridge;

/// <param name="ReplyPrefix">Chat command to answer this message in the same place ("/p ", "/t Name@World "...), or null.</param>
public sealed record HistoryEntry(
    DateTime Time, string Channel, string Sender, TranslationResult Translation, bool Outgoing, string? ReplyPrefix)
{
    /// <summary>The translation (replaced when a skipped message is translated by hand).</summary>
    public TranslationResult Translation { get; set; } = Translation;

    /// <summary>The same message in the learning language, when that option is on (filled in a moment later).</summary>
    public TranslationResult? Learning { get; set; }

    /// <summary>Sent by another player (not you, the clipboard or a Party Finder listing).</summary>
    public bool FromPlayer { get; init; }

    /// <summary>Too long to translate automatically: shown untranslated, with a button to translate it.</summary>
    public bool Skipped { get; set; }

    /// <summary>Raw text with protected spans, kept for a skipped message.</summary>
    public string? PendingText { get; init; }

    public XivChatType Type { get; init; }
}

/// <summary>A message translated and ready to send, exactly as previewed.</summary>
public sealed record OutgoingDraft(string Original, string Prefix, Language? Target, TranslationResult? Result, string Body);
