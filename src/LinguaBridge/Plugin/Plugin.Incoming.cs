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

/// <summary>Incoming chat: which messages are translated, and how translations are printed (below, or in place of the original, in order).</summary>
public sealed partial class Plugin
{
    private readonly Dictionary<string, long> lastMessageBySender = new();
    // Replace mode: translations are printed in arrival order; originals re-printed (on failure) are skipped once.
    private readonly object outputLock = new();
    private Task outputChain = Task.CompletedTask;
    private int pendingOutputs; // messages hidden in Replace mode and not printed yet
    private readonly ConcurrentDictionary<string, DateTime> reprinted = new();

    private bool ReplaceMode => Configuration.IncomingDisplay == IncomingDisplay.Replace ||
                                (Configuration.IncomingDisplay == IncomingDisplay.ReplaceInRoleplay && Configuration.RoleplayMode);

    /// <summary>Matches the tag we put in front of our own printed translations, to never re-translate them.</summary>
    [GeneratedRegex(@"^\[([A-Z?]{2}|PF)>[A-Z]{2}\] ", RegexOptions.CultureInvariant)]
    private static partial Regex OwnTagRegex();

    /// <summary>The short "[FR] " tag (only checked while that option is on, so players' own "[EN]" lines still get translated).</summary>
    [GeneratedRegex(@"^\[[A-Z]{2}\] ", RegexOptions.CultureInvariant)]
    private static partial Regex OwnShortTagRegex();


    private void OnChatMessage(IHandleableChatMessage message)
    {
        try
        {
            var cfg = Configuration;
            // Another plugin already hid it (e.g. a Linkpearl channel set to "hide from game chat"):
            // only translate when asked to, and never hide or re-print the original then.
            var handledElsewhere = message.IsHandled;
            if (!cfg.IncomingEnabled || (handledElsewhere && !cfg.TranslateHandledMessages))
                return;

            var type = message.LogKind;
            var isEmote = type == XivChatType.CustomEmote;
            if (!cfg.Channels.Contains(type) && !(isEmote && cfg.RoleplayMode))
                return;

            var plainText = message.Message.TextValue.Trim();
            if (OwnTagRegex().IsMatch(plainText) || (cfg.ShortTag && OwnShortTagRegex().IsMatch(plainText)))
                return;
            if (reprinted.TryRemove(plainText, out _))
                return; // an original we printed back ourselves (failed translation, or kept in order)

            // Replace mode keeps the chat in order: while translations are pending, the other lines of these channels
            // (short "lol", your own lines, filtered ones) wait their turn instead of jumping above them.
            var replace = ReplaceMode && !handledElsewhere;
            void PassThrough()
            {
                if (replace && Volatile.Read(ref pendingOutputs) > 0)
                    EnqueueOriginal(message, type, plainText);
            }

            if (cfg.PauseInCombat && Condition[ConditionFlag.InCombat])
            {
                PassThrough();
                return;
            }

            var player = message.Sender.Payloads.OfType<PlayerPayload>().FirstOrDefault();
            var senderName = player?.PlayerName ?? StripGlyphs(message.Sender.TextValue);
            var me = PlayerState.CharacterName;
            var isOwn = type == XivChatType.TellOutgoing ||
                        message.SourceKind == XivChatRelationKind.LocalPlayer ||
                        (!string.IsNullOrEmpty(me) && IsSameName(senderName, me));

            // Every line of the conversation (yours included) becomes context for the next translations.
            var convoName = type is XivChatType.TellIncoming or XivChatType.TellOutgoing ? senderName : null;
            if (type == XivChatType.TellIncoming)
                lastTellPartner = senderName;
            var convo = ConversationLog.KeyFor(type, convoName);
            var history = conversations.Recent(convo, cfg.ContextLines, cfg.RoleplayMode ? 3000 : 1500);
            conversations.Add(convo, isOwn ? (string.IsNullOrEmpty(me) ? "Me" : me) : senderName, plainText, isEmote);

            if (isOwn && !cfg.TranslateOwnMessages)
            {
                PassThrough();
                return;
            }

            var text = MessageReader.Read(message.Message);
            if (MessageReader.IsNotWorthTranslating(text) ||
                (cfg.PlayerWhitelist.Count > 0 && !PlayerNames.AnyMatch(cfg.PlayerWhitelist, senderName)) ||
                PlayerNames.AnyMatch(cfg.NeverTranslatePlayers, senderName))
            {
                PassThrough();
                return;
            }

            var world = player?.World.ValueNullable?.Name.ToString();
            var reply = ReplyPrefixFor(type, senderName, world);

            // Too long to translate automatically: listed in /lb, where one click translates it.
            if (plainText.Length > (cfg.RoleplayMode ? cfg.RoleplayMaxMessageLength : cfg.MaxMessageLength))
            {
                var plain = ProtectedText.Plain(text);
                AddHistory(new HistoryEntry(DateTime.Now, ChannelName(type), senderName,
                                            new TranslationResult(plain, plain, Untranslated, Untranslated, string.Empty), false, reply)
                {
                    FromPlayer = !isOwn, Skipped = true, PendingText = text, Type = type,
                });
                PassThrough();
                return;
            }

            // RP posts often arrive as several quick lines: never treat them as macro spam in RP mode.
            if (cfg.FilterMacroSpam && !cfg.RoleplayMode && IsMacroSpam(senderName))
            {
                PassThrough();
                return;
            }

            // "Always translate" players skip the language list (e.g. a friend writing in a language you half read).
            var filter = PlayerNames.AnyMatch(cfg.AlwaysTranslatePlayers, senderName) ? null : cfg.IncomingFilter();
            var senderBytes = message.Sender.Encode();
            var names = new List<string> { senderName };
            if (!string.IsNullOrEmpty(me))
                names.Add(me);
            var ctx = new TranslateRequestContext(history, names);
            var translation = TranslateIncomingAsync(type, senderName, text, reply, ctx, filter, !isOwn);
            var learning = LearningApplies(senderName)
                ? TranslateLearningAsync(translation, type, senderName, text, reply, ctx)
                : null;

            if (replace)
            {
                // Hide the original now; print its translation (or the original, if that fails) in arrival order.
                message.PreventOriginal();
                var originalBytes = message.Message.Encode();
                Interlocked.Increment(ref pendingOutputs);
                lock (outputLock)
                    outputChain = outputChain.ContinueWith(
                        _ => PrintReplacementAsync(translation, learning, type, senderBytes, originalBytes, plainText),
                        TaskScheduler.Default).Unwrap();
            }
            else
            {
                _ = PrintBelowAsync(translation, learning, type, senderBytes);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error while reading chat message");
        }
    }

    /// <summary>Placeholder language of a message that was not translated.</summary>
    private static readonly Language Untranslated = new("??", "?", "", "", "");

    /// <summary>Your own name, even with a glyph in front (party number, cross-world icon), but not "Mika Lo" for "Ka Lo".</summary>
    private static bool IsSameName(string sender, string me) =>
        sender.Equals(me, StringComparison.Ordinal) ||
        (sender.Length > me.Length && sender.EndsWith(me, StringComparison.Ordinal) && !char.IsLetter(sender[^(me.Length + 1)]));

    /// <summary>Replace mode: hides a line and prints it back unchanged, after the translations queued before it.</summary>
    private void EnqueueOriginal(IHandleableChatMessage message, XivChatType type, string plainText)
    {
        message.PreventOriginal();
        var senderBytes = message.Sender.Encode();
        var originalBytes = message.Message.Encode();
        Interlocked.Increment(ref pendingOutputs);
        lock (outputLock)
            outputChain = outputChain.ContinueWith(_ => PrintOriginalAsync(type, senderBytes, originalBytes, plainText),
                                                   TaskScheduler.Default).Unwrap();
    }

    private async Task PrintOriginalAsync(XivChatType type, byte[] senderBytes, byte[] originalBytes, string plainText)
    {
        try
        {
            await Framework.RunOnFrameworkThread(() =>
            {
                reprinted[plainText] = DateTime.Now;
                ChatGui.Print(new XivChatEntry { Type = type, Name = SeString.Parse(senderBytes), Message = SeString.Parse(originalBytes) });
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not print original");
        }
        finally
        {
            Interlocked.Decrement(ref pendingOutputs);
        }
    }

    private bool IsMacroSpam(string sender)
    {
        var now = Environment.TickCount64;
        if (lastMessageBySender.Count > 50)
            foreach (var k in lastMessageBySender.Where(kv => now - kv.Value > 5000).Select(kv => kv.Key).ToList())
                lastMessageBySender.Remove(k);
        var spam = lastMessageBySender.TryGetValue(sender, out var last) && now - last < 650;
        lastMessageBySender[sender] = now;
        return spam;
    }

    /// <summary>History entry with the translation of an incoming message, or null (already in your language, filtered, or failed).</summary>
    private async Task<HistoryEntry?> TranslateIncomingAsync(
        XivChatType type, string senderName, string text, string? reply, TranslateRequestContext ctx, LanguageFilter? filter,
        bool fromPlayer)
    {
        try
        {
            var result = await TranslateIncomingTextAsync(type, senderName, text, ctx, filter).ConfigureAwait(false);
            if (result == null)
                return null;
            var entry = new HistoryEntry(DateTime.Now, ChannelName(type), senderName, result, false, reply) { FromPlayer = fromPlayer, Type = type };
            AddHistory(entry);
            Tutorial.ShowTip(TourContent.FirstTranslation);
            return entry;
        }
        catch (Exception ex)
        {
            ReportError(ex, force: false);
            return null;
        }
    }

    /// <summary>An incoming message, translated the way the settings say (into your language, or both ways).</summary>
    private async Task<TranslationResult?> TranslateIncomingTextAsync(
        XivChatType type, string senderName, string text, TranslateRequestContext? ctx, LanguageFilter? filter)
    {
        // Emotes are "<Name> smiles softly": translate with the name as subject, then remove it again.
        var isEmote = type == XivChatType.CustomEmote;
        var input = isEmote ? ProtectedText.Wrap(senderName) + " " + text : text;
        var result = Configuration.IncomingOnlyToPreferred
            ? await Translator.TranslateAsync(input, Languages.Get(Configuration.PreferredLanguage), filter, ctx).ConfigureAwait(false)
            : await Translator.TranslateAsync(input, null, filter, ctx).ConfigureAwait(false);
        if (result != null && isEmote)
            result = result with { Text = StripLeadingName(result.Text, senderName), Original = ProtectedText.Plain(text) };
        return result;
    }

    /// <summary>Translates a message that was too long to translate automatically (the "Translate" button in /lb).</summary>
    public async Task<bool> TranslateSkippedAsync(HistoryEntry entry)
    {
        if (!entry.Skipped || entry.PendingText == null)
            return false;
        var result = await TranslateIncomingTextAsync(entry.Type, entry.Sender, entry.PendingText, null, null).ConfigureAwait(false);
        if (result == null)
            return false;
        entry.Translation = result;
        entry.Skipped = false;
        history.Changed();
        return true;
    }

    /// <summary>True when the learning line is on and applies to this sender.</summary>
    private bool LearningApplies(string senderName)
    {
        var cfg = Configuration;
        if (string.IsNullOrWhiteSpace(cfg.LearningLanguage) || Languages.TryGet(cfg.LearningLanguage) == null)
            return false;
        return cfg.LearningPlayers.Count == 0 || PlayerNames.AnyMatch(cfg.LearningPlayers, senderName);
    }

    /// <summary>
    /// The same incoming message translated into the learning language (e.g. German shown in Spanish and also
    /// in English), or null when it is not needed: already in that language, or identical to the main translation.
    /// </summary>
    private async Task<TranslationResult?> TranslateLearningAsync(
        Task<HistoryEntry?> main, XivChatType type, string senderName, string text, string? reply, TranslateRequestContext ctx)
    {
        try
        {
            var entry = await main.ConfigureAwait(false);
            var mainResult = entry?.Translation;
            if (mainResult == null && !Configuration.LearningForUntranslated)
                return null;
            var learning = Languages.Get(Configuration.LearningLanguage);
            if (mainResult != null && (mainResult.Source.Code == learning.Code || mainResult.Target.Code == learning.Code))
                return null; // the message is already in that language, or the main line already shows it

            var isEmote = type == XivChatType.CustomEmote;
            var input = isEmote ? ProtectedText.Wrap(senderName) + " " + text : text;
            var result = await Translator.TranslateAsync(input, learning, context: ctx).ConfigureAwait(false);
            if (result == null)
                return null;
            if (isEmote)
                result = result with { Text = StripLeadingName(result.Text, senderName) };
            if (mainResult != null && string.Equals(result.Text.Trim(), mainResult.Text.Trim(), StringComparison.OrdinalIgnoreCase))
                return null;

            if (entry != null)
                entry.Learning = result;
            else
                AddHistory(new HistoryEntry(DateTime.Now, ChannelName(type), senderName, result, false, reply) { FromPlayer = true, Type = type });
            return result;
        }
        catch (Exception ex)
        {
            ReportError(ex, force: false);
            return null;
        }
    }

    private SeString Tagged(TranslationResult result, ushort? color = null) =>
        new SeStringBuilder()
            .AddUiForeground(Configuration.ShortTag ? $"[{result.Target.Code}] " : $"[{result.Source.Code}>{result.Target.Code}] ",
                             color ?? Configuration.TagColor)
            .AddText(ChatSender.FoldForGame(result.Text))
            .Build();

    private async Task PrintBelowAsync(
        Task<HistoryEntry?> translation, Task<TranslationResult?>? learning, XivChatType type, byte[] senderBytes)
    {
        try
        {
            var result = (await translation.ConfigureAwait(false))?.Translation;
            var outType = Configuration.TellsToEcho && type == XivChatType.TellIncoming ? XivChatType.Echo
                        : Configuration.PrintInSameChannel ? type
                        : Configuration.FallbackOutputChannel;
            if (result != null)
                await Framework.RunOnFrameworkThread(() => ChatGui.Print(new XivChatEntry
                {
                    Type = outType,
                    Name = SeString.Parse(senderBytes),
                    Message = Tagged(result),
                })).ConfigureAwait(false);
            await PrintLearningAsync(learning, outType, senderBytes, TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not print translation");
        }
    }

    /// <summary>
    /// Prints the learning-language line (if any) under the translation. If it takes longer than
    /// <paramref name="wait"/>, it is printed later, when it arrives, so nothing waits on it.
    /// </summary>
    private async Task PrintLearningAsync(Task<TranslationResult?>? learning, XivChatType type, byte[] senderBytes, TimeSpan wait)
    {
        if (learning == null)
            return;
        try
        {
            await learning.WaitAsync(wait).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _ = learning.ContinueWith(t => PrintLearningLineAsync(t.Result, type, senderBytes),
                                      TaskContinuationOptions.OnlyOnRanToCompletion).Unwrap();
            return;
        }
        await PrintLearningLineAsync(await learning.ConfigureAwait(false), type, senderBytes).ConfigureAwait(false);
    }

    private async Task PrintLearningLineAsync(TranslationResult? result, XivChatType type, byte[] senderBytes)
    {
        if (result == null)
            return;
        try
        {
            await Framework.RunOnFrameworkThread(() => ChatGui.Print(new XivChatEntry
            {
                Type = type,
                Name = SeString.Parse(senderBytes),
                Message = Tagged(result, Configuration.LearningTagColor),
            })).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not print learning line");
        }
    }

    private async Task PrintReplacementAsync(
        Task<HistoryEntry?> translation, Task<TranslationResult?>? learning, XivChatType type, byte[] senderBytes,
        byte[] originalBytes, string plainText)
    {
        TranslationResult? result = null;
        try
        {
            result = (await translation.WaitAsync(TimeSpan.FromSeconds(12)).ConfigureAwait(false))?.Translation;
        }
        catch (TimeoutException)
        {
            // too slow: show the original rather than keep the conversation waiting
        }

        try
        {
            await Framework.RunOnFrameworkThread(() =>
            {
                if (result == null)
                    reprinted[plainText] = DateTime.Now;
                ChatGui.Print(new XivChatEntry
                {
                    Type = type,
                    Name = SeString.Parse(senderBytes),
                    Message = result != null ? Tagged(result) : SeString.Parse(originalBytes),
                });
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not print replacement");
        }
        finally
        {
            Interlocked.Decrement(ref pendingOutputs);
        }

        // The learning line comes right after its message when it is quick; otherwise it is printed when ready,
        // without holding up the rest of the conversation.
        await PrintLearningAsync(learning, type, senderBytes, TimeSpan.FromSeconds(1.5)).ConfigureAwait(false);

        // Forget stale "re-printed" marks (e.g. the game never echoed one back).
        foreach (var k in reprinted.Where(kv => DateTime.Now - kv.Value > TimeSpan.FromMinutes(1)).Select(kv => kv.Key).ToList())
            reprinted.TryRemove(k, out _);
    }
}
