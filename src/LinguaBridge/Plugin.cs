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

public sealed partial class Plugin : IDalamudPlugin
{
    private const string MainCommand = "/linguabridge";
    private const string ShortCommand = "/lb";
    private const string TranslateCommand = "/tr";
    private const string Tag = "LinguaBridge";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IDtrBar DtrBar { get; private set; } = null!;
    [PluginService] internal static IContextMenu ContextMenu { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;

    public Configuration Configuration { get; }
    public TranslationService Translator { get; }
    public TutorialService Tutorial { get; }

    private readonly WindowSystem windowSystem = new("LinguaBridge");
    private readonly MainWindow mainWindow;
    private readonly ConfigWindow configWindow;
    private readonly SetupWizard setupWizard;
    private readonly LanguagePrompt languagePrompt;
    private IDtrBarEntry? dtrEntry;

    private readonly ConcurrentQueue<HistoryEntry> history = new();
    private long historyVersion;
    private readonly Dictionary<string, long> lastMessageBySender = new();
    private readonly ConversationLog conversations = new();
    private string? lastTellPartner;

    // Replace mode: translations are printed in arrival order; originals re-printed (on failure) are skipped once.
    private readonly object outputLock = new();
    private Task outputChain = Task.CompletedTask;
    private int pendingOutputs; // messages hidden in Replace mode and not printed yet
    private readonly ConcurrentDictionary<string, DateTime> reprinted = new();

    private bool ReplaceMode => Configuration.IncomingDisplay == IncomingDisplay.Replace ||
                                (Configuration.IncomingDisplay == IncomingDisplay.ReplaceInRoleplay && Configuration.RoleplayMode);
    private DateTime lastErrorPrinted = DateTime.MinValue;
    private DateTime lastFallbackPrinted = DateTime.MinValue;

    /// <summary>Matches the tag we put in front of our own printed translations, to never re-translate them.</summary>
    [GeneratedRegex(@"^\[([A-Z?]{2}|PF)>[A-Z]{2}\] ", RegexOptions.CultureInvariant)]
    private static partial Regex OwnTagRegex();

    /// <summary>The short "[FR] " tag (only checked while that option is on, so players' own "[EN]" lines still get translated).</summary>
    [GeneratedRegex(@"^\[[A-Z]{2}\] ", RegexOptions.CultureInvariant)]
    private static partial Regex OwnShortTagRegex();

    private const string AetherphoneInternalName = "Aetherphone";
    private bool aetherphoneLoaded;
    private long aetherphoneCheckedAt; // 0 = never checked

    /// <summary>Translations waiting to be put on the clipboard (ImGui can only do that during Draw).</summary>
    private readonly ConcurrentQueue<string> pendingClipboard = new();

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Loc.SetLanguage(string.IsNullOrEmpty(Configuration.UiLanguage) ? Loc.Detect(PluginInterface.UiLanguage) : Configuration.UiLanguage);
        Translator = new TranslationService(() => Configuration);
        if (Configuration.Migrate())
            PluginInterface.SavePluginConfig(Configuration);
        Translator.FallbackUsed += OnFallbackUsed;
        Translator.DeepL.KeyUnavailable += OnDeepLKeyUnavailable;
        Translator.DeepL.SaveRequested += OnGlossarySaved;
        Translator.Llm.ProviderUnavailable += OnLlmProviderUnavailable;
        Translator.Google.Paused += OnGooglePaused;
        Translator.Google.Resumed += OnGoogleResumed;

        Tutorial = new TutorialService(this);
        mainWindow = new MainWindow(this);
        configWindow = new ConfigWindow(this);
        setupWizard = new SetupWizard(this);
        languagePrompt = new LanguagePrompt(this);
        windowSystem.AddWindow(mainWindow);
        windowSystem.AddWindow(configWindow);
        windowSystem.AddWindow(setupWizard);
        windowSystem.AddWindow(languagePrompt);

        var mainHelp = new CommandInfo(OnMainCommand)
        {
            HelpMessage = L("Open the LinguaBridge window. Add: config | wizard | tour [settings] | tips on|off|reset | on | off | toggle | rp (roleplay mode)."),
        };
        CommandManager.AddHandler(MainCommand, mainHelp);
        CommandManager.AddHandler(ShortCommand, new CommandInfo(OnMainCommand) { HelpMessage = L("Short for /linguabridge."), ShowInHelp = false });
        CommandManager.AddHandler(TranslateCommand, new CommandInfo(OnTranslateCommand)
        {
            HelpMessage = L("Translate and send: /tr [:fr|:en|...] [/p|/fc|/t Name@World|...] message. " +
                            "Without a channel it goes to your current chat channel."),
        });

        PluginInterface.UiBuilder.Draw += windowSystem.Draw;
        PluginInterface.UiBuilder.Draw += Tutorial.Draw;
        PluginInterface.UiBuilder.Draw += FlushClipboard;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        ChatGui.ChatMessage += OnChatMessage;
        ContextMenu.OnMenuOpened += OnContextMenuOpened;

        Ui.ReduceMotion = Configuration.ReduceMotion;
        UpdateDtr();
        if (!Configuration.ShowedWizard)
            setupWizard.IsOpen = true;
        else if (!Configuration.UiLanguageChosen)
            languagePrompt.IsOpen = true; // existing install updated to a version with translations: ask once
    }

    public void Dispose()
    {
        ChatGui.ChatMessage -= OnChatMessage;
        ContextMenu.OnMenuOpened -= OnContextMenuOpened;
        PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        PluginInterface.UiBuilder.Draw -= Tutorial.Draw;
        PluginInterface.UiBuilder.Draw -= FlushClipboard;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        CommandManager.RemoveHandler(MainCommand);
        CommandManager.RemoveHandler(ShortCommand);
        CommandManager.RemoveHandler(TranslateCommand);
        dtrEntry?.Remove();
        windowSystem.RemoveAllWindows();
        Translator.FallbackUsed -= OnFallbackUsed;
        Translator.DeepL.KeyUnavailable -= OnDeepLKeyUnavailable;
        Translator.DeepL.SaveRequested -= OnGlossarySaved;
        Translator.Llm.ProviderUnavailable -= OnLlmProviderUnavailable;
        Translator.Google.Paused -= OnGooglePaused;
        Translator.Google.Resumed -= OnGoogleResumed;
        Translator.Dispose();
    }

    public void SaveConfig()
    {
        Ui.ReduceMotion = Configuration.ReduceMotion;
        PluginInterface.SavePluginConfig(Configuration);
        UpdateDtr();
    }

    public void ToggleMainUi() => mainWindow.Toggle();
    public void ToggleConfigUi() => configWindow.Toggle();
    public void OpenWizard() => setupWizard.IsOpen = true;

    /// <summary>Switches the plugin's interface language right away and remembers it.</summary>
    public void SetUiLanguage(string code)
    {
        Configuration.UiLanguage = code;
        Configuration.UiLanguageChosen = true;
        Loc.SetLanguage(code);
        SaveConfig();
    }

    // ---------------------------------------------------------------- tutorial hooks

    public bool WizardOpen => setupWizard.IsOpen;
    public bool InCombat => Condition[ConditionFlag.InCombat];
    public bool MainWindowHasInput => mainWindow.HasInput;
    public bool MainWindowHasPreview => mainWindow.HasPreview;

    public bool IsWindowOpen(TourWindow window) => window switch
    {
        TourWindow.Main => mainWindow.IsOpen,
        TourWindow.Config => configWindow.IsOpen,
        _ => true,
    };

    public void OpenWindow(TourWindow window)
    {
        if (window == TourWindow.Main)
            mainWindow.IsOpen = true;
        else if (window == TourWindow.Config)
            configWindow.IsOpen = true;
    }

    public IReadOnlyList<HistoryEntry> History => history.Reverse().ToList();

    /// <summary>Goes up by one for every entry added (the count stops changing once the history is full).</summary>
    public long HistoryVersion => Interlocked.Read(ref historyVersion);

    public void ClearHistory() => history.Clear();

    private void AddHistory(HistoryEntry entry)
    {
        history.Enqueue(entry);
        while (history.Count > Math.Max(10, Configuration.HistorySize) && history.TryDequeue(out _)) { }
        Interlocked.Increment(ref historyVersion);
    }

    public void SetRoleplayMode(bool enabled, bool announce)
    {
        Configuration.RoleplayMode = enabled;
        Translator.ClearCache();
        SaveConfig();
        if (announce)
            ChatGui.Print(enabled
                ? L("Roleplay mode ON: emotes, *actions*, \"speech\" and ((OOC)) are handled for RP.")
                : L("Roleplay mode OFF."), Tag);
        if (enabled)
            Tutorial.ShowTip(TourContent.FirstRoleplay);
    }

    public void SetIncomingEnabled(bool enabled, bool announce)
    {
        Configuration.IncomingEnabled = enabled;
        SaveConfig();
        if (announce)
            ChatGui.Print(enabled ? L("Chat translation ON.") : L("Chat translation OFF."), Tag);
    }

    // ---------------------------------------------------------------- server info bar

    private void UpdateDtr()
    {
        if (!Configuration.ShowDtrEntry)
        {
            dtrEntry?.Remove();
            dtrEntry = null;
            return;
        }

        dtrEntry ??= DtrBar.Get(Tag);
        var on = Configuration.IncomingEnabled;
        var engine = Translator.ActiveEngineName;
        var pausedTag = engine == "Google" && Translator.Google.PauseRemaining != null ? " (" + L("paused") + ")" : string.Empty;
        dtrEntry.Text = new SeStringBuilder()
                        .AddUiForeground(on ? (ushort)45 : (ushort)4)
                        .AddText(on ? $"LB {(Configuration.RoleplayMode ? "RP " : "")}{Configuration.LanguageA}/{Configuration.LanguageB}{pausedTag}" : "LB off")
                        .AddUiForegroundOff()
                        .Build();
        var paused = Translator.Google.PauseRemaining is { } left
            ? "\n" + LF("Google paused (too many requests): resumes by itself at {0:HH:mm}.", DateTime.Now + left)
            : string.Empty;
        dtrEntry.Tooltip = new SeString(new TextPayload(
            (on ? LF("LinguaBridge: chat translation ON ({0})", engine)
                : LF("LinguaBridge: chat translation OFF ({0})", engine)) + paused + "\n" +
            L("Left click: toggle   Right click: open window")));
        dtrEntry.OnClick = e =>
        {
            if (e.ClickType == MouseClickType.Right)
                ToggleMainUi();
            else
                SetIncomingEnabled(!Configuration.IncomingEnabled, announce: false);
        };
        dtrEntry.Shown = true;
    }

    // ---------------------------------------------------------------- commands

    private void OnMainCommand(string command, string args)
    {
        var trimmed = args.Trim();
        if (trimmed.Equals("clip", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("clip ", StringComparison.OrdinalIgnoreCase))
        {
            _ = TranslateClipboardCommandAsync(trimmed[4..].Trim());
            return;
        }

        switch (trimmed.ToLowerInvariant())
        {
            case "config" or "settings":
                ToggleConfigUi();
                break;
            case "wizard" or "setup":
                OpenWizard();
                break;
            case "tour" or "tutorial" or "guide":
                Tutorial.StartTour(TourContent.MainTourId);
                break;
            case "tour settings" or "tour config" or "tutorial settings" or "guide settings":
                Tutorial.StartTour(TourContent.SettingsTourId);
                break;
            case "tips on":
                Tutorial.SetTipsEnabled(true);
                ChatGui.Print(L("Tips ON."), Tag);
                break;
            case "tips off":
                Tutorial.SetTipsEnabled(false);
                ChatGui.Print(L("Tips OFF."), Tag);
                break;
            case "tips reset":
                Tutorial.ResetTips();
                ChatGui.Print(L("All tips and tour offers will be shown again."), Tag);
                break;
            case "on":
                SetIncomingEnabled(true, announce: true);
                break;
            case "off":
                SetIncomingEnabled(false, announce: true);
                break;
            case "toggle":
                SetIncomingEnabled(!Configuration.IncomingEnabled, announce: true);
                break;
            case "rp" or "roleplay":
                SetRoleplayMode(!Configuration.RoleplayMode, announce: true);
                break;
            default:
                ToggleMainUi();
                break;
        }
    }

    private void OnTranslateCommand(string command, string args)
    {
        if (string.IsNullOrWhiteSpace(args))
        {
            ToggleMainUi();
            return;
        }

        var parsed = OutgoingCommand.Parse(args, out var error);
        if (parsed == null)
        {
            ChatGui.PrintError(error ?? L("Could not parse the command."), Tag);
            return;
        }

        _ = TranslateAndSendAsync(parsed.Text, parsed.Target, parsed.ChannelPrefix);
    }

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

    private static bool IsEmotePrefix(string prefix) =>
        prefix.Trim().Equals("/em", StringComparison.OrdinalIgnoreCase) ||
        prefix.Trim().Equals("/emote", StringComparison.OrdinalIgnoreCase);

    /// <summary>Japanese topic / subject particles left after the name ("Nameは微笑む" -> "微笑む").</summary>
    private static readonly string[] JapaneseParticles = { "は、", "が、", "は", "が", "も", "さんは", "さんが" };

    private static string StripLeadingName(string text, string name)
    {
        var t = text.TrimStart();
        string rest;
        if (t.StartsWith(name, StringComparison.OrdinalIgnoreCase))
            rest = t[name.Length..];
        else if (name.Split(' ')[0] is { Length: >= 3 } first &&
                 (t.StartsWith(first + " ", StringComparison.OrdinalIgnoreCase) ||
                  (t.StartsWith(first, StringComparison.OrdinalIgnoreCase) && t.Length > first.Length && !char.IsLetter(t[first.Length]))))
            rest = t[first.Length..];
        else
            return t;

        // Japanese: no space after the name, a particle instead.
        foreach (var particle in JapaneseParticles)
            if (rest.StartsWith(particle, StringComparison.Ordinal))
                return rest[particle.Length..].TrimStart();
        // German / Dutch / English possessive or case forms glued to the name ("Name's", "Names") are not stripped.
        if (rest.Length > 0 && char.IsLetter(rest[0]))
            return t;
        return rest.TrimStart(' ', ',', '、');
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

    // ---------------------------------------------------------------- incoming chat

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
        Interlocked.Increment(ref historyVersion);
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

    // ---------------------------------------------------------------- Aetherphone

    /// <summary>True while the Aetherphone plugin (whose Linkpearl app mirrors the game chat) is loaded. Checked at most every 5 s.</summary>
    public bool AetherphoneLoaded
    {
        get
        {
            var now = Environment.TickCount64;
            if (aetherphoneCheckedAt != 0 && now - aetherphoneCheckedAt < 5000)
                return aetherphoneLoaded;
            aetherphoneCheckedAt = now;
            try
            {
                aetherphoneLoaded = PluginInterface.InstalledPlugins.Any(
                    p => p.IsLoaded && p.InternalName.Equals(AetherphoneInternalName, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Could not list installed plugins");
                aetherphoneLoaded = false;
            }
            return aetherphoneLoaded;
        }
    }

    // ---------------------------------------------------------------- translate only (clipboard)

    /// <summary>
    /// Translates text without sending it anywhere (for Aetherphone's ChocoChat / Velvet, websites, Discord...).
    /// With no target: into the language you read best, or, if it is already in that language, into the other
    /// language of the pair (so the same button works for reading and for writing).
    /// Returns null when there is nothing to translate.
    /// </summary>
    public async Task<TranslationResult?> TranslateTextAsync(string text, Language? target)
    {
        text = text.Trim();
        if (text.Length == 0)
            return null;
        if (target != null)
            return await Translator.TranslateAsync(text, target).ConfigureAwait(false);
        var preferred = Languages.Get(Configuration.PreferredLanguage);
        return await Translator.TranslateAsync(text, preferred).ConfigureAwait(false)
               ?? await Translator.TranslateAsync(text).ConfigureAwait(false);
    }

    /// <summary>Puts text on the clipboard on the next frame.</summary>
    public void CopyToClipboard(string text)
    {
        if (!string.IsNullOrEmpty(text))
            pendingClipboard.Enqueue(text);
    }

    private void FlushClipboard()
    {
        string? last = null;
        while (pendingClipboard.TryDequeue(out var text))
            last = text;
        if (last != null)
            Dalamud.Bindings.ImGui.ImGui.SetClipboardText(last);
    }

    /// <summary>/lb clip [:xx] : translates the clipboard, shows the result (only to you) and copies it back.</summary>
    private async Task TranslateClipboardCommandAsync(string args)
    {
        try
        {
            Language? target = null;
            if (args.StartsWith(':'))
            {
                target = Languages.TryGet(args[1..].Trim().ToUpperInvariant());
                if (target == null)
                {
                    ChatGui.PrintError(LF("Unknown language \"{0}\". Example: /lb clip :fr", args[1..].Trim()), Tag);
                    return;
                }
            }

            var text = Clipboard.GetText()?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                ChatGui.PrintError(L("The clipboard has no text. Copy a message first (Ctrl+C), then /lb clip."), Tag);
                return;
            }
            if (text.Length > 3000)
                text = text[..3000];

            var result = await TranslateTextAsync(text, target).ConfigureAwait(false);
            if (result == null)
            {
                await Framework.RunOnFrameworkThread(() => ChatGui.Print(L("Nothing to translate: the text is already in that language."), Tag))
                               .ConfigureAwait(false);
                return;
            }

            CopyToClipboard(result.Text);
            AddHistory(new HistoryEntry(DateTime.Now, "clipboard", "Clipboard", result, false, null));
            await Framework.RunOnFrameworkThread(() => ChatGui.Print(new XivChatEntry
            {
                Type = XivChatType.Echo,
                Message = new SeStringBuilder()
                          .AddUiForeground($"[{result.Source.Code}>{result.Target.Code}] ", Configuration.TagColor)
                          .AddText(ChatSender.FoldForGame(result.Text))
                          .AddUiForeground(" (" + L("copied") + ")", 4)
                          .Build(),
            })).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ReportError(ex, force: true);
        }
    }

    // ---------------------------------------------------------------- party finder

    private void OnContextMenuOpened(IMenuOpenedArgs args)
    {
        if (!Configuration.PartyFinderMenu || args.AddonName != "LookingForGroupDetail")
            return;
        args.AddMenuItem(new MenuItem
        {
            Name = L("Translate description"),
            UseDefaultPrefix = true,
            OnClicked = TranslatePartyFinder,
        });
    }

    private unsafe void TranslatePartyFinder(IMenuItemClickedArgs args)
    {
        try
        {
            var addon = (AddonLookingForGroupDetail*)args.AddonPtr;
            if (addon == null)
                return;
            var description = SeString.Parse(addon->DescriptionString.AsSpan()).TextValue;
            description = StripGlyphs(description).Trim();
            if (description.Length == 0)
            {
                ChatGui.Print(L("This listing has no description."), Tag);
                return;
            }
            _ = TranslatePartyFinderAsync(description);
        }
        catch (Exception ex)
        {
            ReportError(ex, force: true);
        }
    }

    private async Task TranslatePartyFinderAsync(string description)
    {
        try
        {
            var preferred = Languages.Get(Configuration.PreferredLanguage);
            var result = await Translator.TranslateAsync(description, preferred).ConfigureAwait(false);
            if (result == null)
            {
                await Framework.RunOnFrameworkThread(() =>
                    ChatGui.Print(LF("The description is already in {0}.", L(preferred.Name)), Tag));
                return;
            }

            AddHistory(new HistoryEntry(DateTime.Now, "Party Finder", "Listing", result, false, null));
            Tutorial.ShowTip(TourContent.FirstPartyFinder);
            await Framework.RunOnFrameworkThread(() => ChatGui.Print(new XivChatEntry
            {
                Type = XivChatType.Echo,
                Message = new SeStringBuilder()
                          .AddUiForeground($"[PF>{result.Target.Code}] ", Configuration.TagColor)
                          .AddText(ChatSender.FoldForGame(result.Text))
                          .Build(),
            }));
        }
        catch (Exception ex)
        {
            ReportError(ex, force: true);
        }
    }

    // ---------------------------------------------------------------- helpers

    private void OnFallbackUsed(string reason)
    {
        Tutorial.ShowTip(TourContent.FirstFallback);
        if (DateTime.Now - lastFallbackPrinted < TimeSpan.FromMinutes(5))
            return;
        lastFallbackPrinted = DateTime.Now;
        _ = Framework.RunOnFrameworkThread(() =>
            ChatGui.PrintError(LF("{0}. Using the backup engine for now.", reason), Tag));
    }

    private readonly Dictionary<string, DateTime> llmNoticeTimes = new();

    private void OnLlmProviderUnavailable(string provider, string reason)
    {
        lock (llmNoticeTimes)
        {
            if (llmNoticeTimes.TryGetValue(provider, out var t) && DateTime.Now - t < TimeSpan.FromMinutes(5))
                return;
            llmNoticeTimes[provider] = DateTime.Now;
        }
        _ = Framework.RunOnFrameworkThread(() => ChatGui.Print(LF("AI {0}: {1}, trying the next one.", provider, reason), Tag));
    }

    private void OnGooglePaused(TimeSpan duration, string reason)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling(duration.TotalMinutes));
        _ = Framework.RunOnFrameworkThread(() =>
        {
            UpdateDtr();
            if (Translator.ActiveEngineName != "Google")
            {
                Log.Information($"Google (backup) paused for {minutes} min: {reason}");
                return;
            }
            var backup = Translator.DeepL.Keys.Count > 0 ? "DeepL" : Translator.Llm.ActiveProviders.Count > 0 ? L("AI") : null;
            ChatGui.Print(backup != null
                ? LF("Google is limiting requests ({0}): it pauses for {1} min and resumes by itself. {2} translates meanwhile.",
                     reason, minutes, backup)
                : LF("Google is limiting requests ({0}): translation pauses for {1} min and resumes by itself. " +
                     "Messages are shown untranslated meanwhile.", reason, minutes), Tag);
        });
    }

    private void OnGoogleResumed() => _ = Framework.RunOnFrameworkThread(() =>
    {
        UpdateDtr();
        if (Translator.ActiveEngineName == "Google")
            ChatGui.Print(L("Google answers again: translation resumed."), Tag);
    });

    private void OnGlossarySaved() => _ = Framework.RunOnFrameworkThread(() => PluginInterface.SavePluginConfig(Configuration));

    private void OnDeepLKeyUnavailable(int index, string reason)
    {
        var msg = index + 2 <= Translator.DeepL.Keys.Count
            ? LF("DeepL key #{0}: {1}, trying key #{2}.", index + 1, reason, index + 2)
            : LF("DeepL key #{0}: {1}, trying the remaining keys.", index + 1, reason);
        _ = Framework.RunOnFrameworkThread(() => ChatGui.Print(msg, Tag));
    }

    private void ReportError(Exception ex, bool force)
    {
        // A planned pause (Google rate limit): announced once when it starts, not for every message.
        if (ex is EnginePausedException && !force)
        {
            Log.Debug(ex.Message);
            return;
        }
        var msg = ex is TranslationException or ArgumentException ? ex.Message : LF("Unexpected error: {0}", ex.Message);
        if (ex is not TranslationException and not ArgumentException)
            Log.Error(ex, "LinguaBridge error");
        else
            Log.Warning(msg);

        // Don't spam the chat when the engine is down: at most one error per 30 s for incoming messages.
        if (!force && DateTime.Now - lastErrorPrinted < TimeSpan.FromSeconds(30))
            return;
        lastErrorPrinted = DateTime.Now;
        _ = Framework.RunOnFrameworkThread(() => ChatGui.PrintError(msg, Tag));
    }

    private static string StripGlyphs(string s) =>
        new string(s.Where(c => c is < '' or > '').ToArray()).Trim();

    /// <summary>The chat command that answers a message in the same place it came from.</summary>
    public static string? ReplyPrefixFor(XivChatType type, string sender, string? world) => type switch
    {
        XivChatType.Say => "/s ",
        XivChatType.Yell => "/y ",
        XivChatType.Shout => "/sh ",
        XivChatType.Party or XivChatType.CrossParty => "/p ",
        XivChatType.Alliance => "/a ",
        XivChatType.FreeCompany => "/fc ",
        XivChatType.CustomEmote => "/em ",
        XivChatType.NoviceNetwork => "/n ",
        XivChatType.PvPTeam => "/pvpteam ",
        XivChatType.TellIncoming => string.IsNullOrEmpty(world) ? "/r " : $"/t {sender}@{world} ",
        >= XivChatType.Ls1 and <= XivChatType.Ls8 => $"/l{type - XivChatType.Ls1 + 1} ",
        XivChatType.CrossLinkShell1 => "/cwl1 ",
        >= XivChatType.CrossLinkShell2 and <= XivChatType.CrossLinkShell8 => $"/cwl{type - XivChatType.CrossLinkShell2 + 2} ",
        _ => null,
    };

    public static string ChannelName(XivChatType type) => type switch
    {
        XivChatType.TellIncoming => "Tell",
        XivChatType.TellOutgoing => "Tell (out)",
        XivChatType.CrossParty => "Party",
        XivChatType.FreeCompany => "FC",
        XivChatType.CustomEmote => "Emote",
        >= XivChatType.Ls1 and <= XivChatType.Ls8 => $"LS{type - XivChatType.Ls1 + 1}",
        XivChatType.CrossLinkShell1 => "CWLS1",
        >= XivChatType.CrossLinkShell2 and <= XivChatType.CrossLinkShell8 => $"CWLS{type - XivChatType.CrossLinkShell2 + 2}",
        _ => type.ToString(),
    };
}
