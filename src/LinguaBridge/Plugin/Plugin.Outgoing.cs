using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.Chat;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Command;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using LinguaBridge.Chat;
using LinguaBridge.Translation;
using LinguaBridge.Tutorial;
using LinguaBridge.Localization;
using LinguaBridge.Windows;
using static LinguaBridge.Localization.Loc;

namespace LinguaBridge;

/// <summary>Sending your own messages translated: prepare (also used by the preview), split and send.</summary>
public sealed partial class Plugin
{
    /// <summary>Translates <paramref name="text"/> and sends it to the chat (prefix e.g. "/p ").</summary>
    public async Task<bool> TranslateAndSendAsync(string text, Language? target, string channelPrefix)
    {
        OutgoingDraft draft;
        try
        {
            draft = await PrepareOutgoingAsync(text, target, channelPrefix).ConfigureAwait(false);
        }
        catch (EnginePausedException ex)
        {
            _ = Framework.RunOnFrameworkThread(() => ChatGui.PrintError(LF("{0}. Your message was not sent.", ex.Message), Tag));
            return false;
        }
        catch (Exception ex)
        {
            ReportError(ex, force: true);
            return false;
        }
        return await SendDraftAsync(draft).ConfigureAwait(false);
    }

    /// <summary>
    /// Translates a message to send, with the same context, names and emote handling as sending it. The preview
    /// shows exactly this, and <see cref="SendDraftAsync"/> sends exactly this (no second, different translation).
    /// </summary>
    public async Task<OutgoingDraft> PrepareOutgoingAsync(string text, Language? target, string channelPrefix)
    {
        var me = PlayerState.CharacterName;
        var isEmote = IsEmotePrefix(channelPrefix);
        var convo = ConversationKeyForPrefix(channelPrefix);
        var names = new List<string> { me };
        if (convo?.StartsWith("tell:", StringComparison.Ordinal) == true)
            names.Add(convo[5..]);
        var ctx = new TranslateRequestContext(convo == null ? null : conversations.Recent(convo, Configuration.ContextLines), names);

        // Emotes are third-person fragments ("smiles softly"): give the engine a subject, then drop it again.
        var input = isEmote && !string.IsNullOrEmpty(me) ? ProtectedText.Wrap(me) + " " + text : text;
        var result = await Translator.TranslateAsync(input, target, context: ctx).ConfigureAwait(false);
        var translated = result?.Text ?? text;
        if (isEmote && !string.IsNullOrEmpty(me))
            translated = StripLeadingName(translated, me);
        var body = ChatSender.Clean(translated);
        if (result != null)
            result = result with { Original = text, Text = body };
        return new OutgoingDraft(text, channelPrefix, target, result, body);
    }

    /// <summary>Sends a prepared message (split into several lines when needed).</summary>
    public async Task<bool> SendDraftAsync(OutgoingDraft draft)
    {
        try
        {
            var lines = Configuration.SplitLongMessages
                ? ChatSender.Split(draft.Prefix, draft.Body, Configuration.SplitMarkers)
                : new List<string> { draft.Prefix + draft.Body };

            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                await Framework.RunOnFrameworkThread(() => ChatSender.Send(line)).ConfigureAwait(false);
                if (i < lines.Count - 1)
                    await Task.Delay(1200).ConfigureAwait(false); // stay clear of the game's chat spam limit
            }

            var result = draft.Result;
            if (Configuration.EchoOriginalOnSend && result != null)
                await Framework.RunOnFrameworkThread(() => ChatGui.Print(new XivChatEntry
                {
                    Type = XivChatType.Echo,
                    Message = new SeStringBuilder()
                              .AddUiForeground($"[{result.Source.Code}>{result.Target.Code}] ", Configuration.TagColor)
                              .AddText("(" + L("original") + ") " + ChatSender.FoldForGame(draft.Original))
                              .Build(),
                })).ConfigureAwait(false);

            if (result != null)
            {
                AddHistory(new HistoryEntry(DateTime.Now, draft.Prefix.Trim() is { Length: > 0 } p ? p : "current", "You",
                                            result, true, null));
                Tutorial.ShowTip(TourContent.FirstSend);
            }
            return true;
        }
        catch (Exception ex)
        {
            ReportError(ex, force: true);
            return false;
        }
    }

    /// <summary>Conversation that a "/p ", "/t Name@World " ... prefix writes to (for context), or null.</summary>
    private string? ConversationKeyForPrefix(string prefix)
    {
        var p = prefix.Trim();
        if (p.Length == 0)
            return null;
        var cmd = p.Split(' ')[0].ToLowerInvariant();
        switch (cmd)
        {
            case "/t" or "/tell":
                var target = p[cmd.Length..].Trim();
                var at = target.IndexOf('@');
                return target.StartsWith('<') ? null : "tell:" + (at > 0 ? target[..at] : target);
            case "/r" or "/reply":
                return lastTellPartner == null ? null : "tell:" + lastTellPartner;
            case "/em" or "/emote" or "/s" or "/say":
                return ConversationLog.KeyFor(XivChatType.Say, null);
        }
        foreach (XivChatType t in Enum.GetValues(typeof(XivChatType)))
            if (ReplyPrefixFor(t, string.Empty, null)?.Trim() == p)
                return ConversationLog.KeyFor(t, null);
        return null;
    }
}
