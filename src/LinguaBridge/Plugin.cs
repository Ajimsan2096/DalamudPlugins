using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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
using LinguaBridge.Windows;

namespace LinguaBridge;

/// <param name="ReplyPrefix">Chat command to answer this message in the same place ("/p ", "/t Name@World "...), or null.</param>
public sealed record HistoryEntry(
    DateTime Time, string Channel, string Sender, TranslationResult Translation, bool Outgoing, string? ReplyPrefix);

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

    public Configuration Configuration { get; }
    public TranslationService Translator { get; }

    private readonly WindowSystem windowSystem = new("LinguaBridge");
    private readonly MainWindow mainWindow;
    private readonly ConfigWindow configWindow;
    private readonly SetupWizard setupWizard;
    private IDtrBarEntry? dtrEntry;

    private readonly ConcurrentQueue<HistoryEntry> history = new();
    private readonly Dictionary<string, long> lastMessageBySender = new();
    private DateTime lastErrorPrinted = DateTime.MinValue;
    private DateTime lastFallbackPrinted = DateTime.MinValue;

    /// <summary>Matches the tag we put in front of our own printed translations, to never re-translate them.</summary>
    [GeneratedRegex(@"^\[([A-Z?]{2}|PF)>[A-Z]{2}\] ", RegexOptions.CultureInvariant)]
    private static partial Regex OwnTagRegex();

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Translator = new TranslationService(() => Configuration);
        if (Configuration.Migrate())
            PluginInterface.SavePluginConfig(Configuration);
        Translator.FallbackUsed += OnFallbackUsed;
        Translator.DeepL.KeyUnavailable += OnDeepLKeyUnavailable;

        mainWindow = new MainWindow(this);
        configWindow = new ConfigWindow(this);
        setupWizard = new SetupWizard(this);
        windowSystem.AddWindow(mainWindow);
        windowSystem.AddWindow(configWindow);
        windowSystem.AddWindow(setupWizard);

        var mainHelp = new CommandInfo(OnMainCommand)
        {
            HelpMessage = "Open the LinguaBridge window. Add: config | wizard | on | off | toggle.",
        };
        CommandManager.AddHandler(MainCommand, mainHelp);
        CommandManager.AddHandler(ShortCommand, new CommandInfo(OnMainCommand) { HelpMessage = "Short for /linguabridge.", ShowInHelp = false });
        CommandManager.AddHandler(TranslateCommand, new CommandInfo(OnTranslateCommand)
        {
            HelpMessage = "Translate and send: /tr [:fr|:en|...] [/p|/fc|/t Name@World|...] message. " +
                          "Without a channel it goes to your current chat channel.",
        });

        PluginInterface.UiBuilder.Draw += windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        ChatGui.ChatMessage += OnChatMessage;
        ContextMenu.OnMenuOpened += OnContextMenuOpened;

        UpdateDtr();
        if (!Configuration.ShowedWizard)
            setupWizard.IsOpen = true;
    }

    public void Dispose()
    {
        ChatGui.ChatMessage -= OnChatMessage;
        ContextMenu.OnMenuOpened -= OnContextMenuOpened;
        PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        CommandManager.RemoveHandler(MainCommand);
        CommandManager.RemoveHandler(ShortCommand);
        CommandManager.RemoveHandler(TranslateCommand);
        dtrEntry?.Remove();
        windowSystem.RemoveAllWindows();
        Translator.FallbackUsed -= OnFallbackUsed;
        Translator.DeepL.KeyUnavailable -= OnDeepLKeyUnavailable;
        Translator.Dispose();
    }

    public void SaveConfig()
    {
        PluginInterface.SavePluginConfig(Configuration);
        UpdateDtr();
    }

    public void ToggleMainUi() => mainWindow.Toggle();
    public void ToggleConfigUi() => configWindow.Toggle();
    public void OpenWizard() => setupWizard.IsOpen = true;

    public IReadOnlyList<HistoryEntry> History => history.Reverse().ToList();
    public void ClearHistory() => history.Clear();

    private void AddHistory(HistoryEntry entry)
    {
        history.Enqueue(entry);
        while (history.Count > Math.Max(10, Configuration.HistorySize) && history.TryDequeue(out _)) { }
    }

    public void SetIncomingEnabled(bool enabled, bool announce)
    {
        Configuration.IncomingEnabled = enabled;
        SaveConfig();
        if (announce)
            ChatGui.Print(enabled ? "Chat translation ON." : "Chat translation OFF.", Tag);
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
        dtrEntry.Text = new SeStringBuilder()
                        .AddUiForeground(on ? (ushort)45 : (ushort)4)
                        .AddText(on ? $"LB {Configuration.LanguageA}/{Configuration.LanguageB}" : "LB off")
                        .AddUiForegroundOff()
                        .Build();
        dtrEntry.Tooltip = new SeString(new TextPayload(
            $"LinguaBridge: chat translation {(on ? "ON" : "OFF")} ({Configuration.Engine})\n" +
            "Left click: toggle   Right click: open window"));
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
        switch (args.Trim().ToLowerInvariant())
        {
            case "config" or "settings":
                ToggleConfigUi();
                break;
            case "wizard" or "setup":
                OpenWizard();
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
            ChatGui.PrintError(error ?? "Could not parse the command.", Tag);
            return;
        }

        _ = TranslateAndSendAsync(parsed.Text, parsed.Target, parsed.ChannelPrefix);
    }

    /// <summary>Translates <paramref name="text"/> and sends it to the chat (prefix e.g. "/p ").</summary>
    public async Task<bool> TranslateAndSendAsync(string text, Language? target, string channelPrefix)
    {
        try
        {
            var result = await Translator.TranslateAsync(text, target, translateOthers: true).ConfigureAwait(false);
            var body = ChatSender.Clean(result?.Text ?? text);
            var line = channelPrefix + body;

            await Framework.RunOnFrameworkThread(() =>
            {
                ChatSender.Send(line);
                if (Configuration.EchoOriginalOnSend && result != null)
                    ChatGui.Print(new XivChatEntry
                    {
                        Type = XivChatType.Echo,
                        Message = new SeStringBuilder()
                                  .AddUiForeground($"[{result.Source.Code}>{result.Target.Code}] ", Configuration.TagColor)
                                  .AddText($"(original) {ChatSender.FoldForGame(text)}")
                                  .Build(),
                    });
            }).ConfigureAwait(false);

            if (result != null)
                AddHistory(new HistoryEntry(DateTime.Now, channelPrefix.Trim() is { Length: > 0 } p ? p : "current", "You",
                                            result, true, null));
            return true;
        }
        catch (Exception ex)
        {
            ReportError(ex, force: true);
            return false;
        }
    }

    // ---------------------------------------------------------------- incoming chat

    private void OnChatMessage(IHandleableChatMessage message)
    {
        try
        {
            var cfg = Configuration;
            if (!cfg.IncomingEnabled || message.IsHandled)
                return;

            var type = message.LogKind;
            if (!cfg.Channels.Contains(type))
                return;
            if (cfg.PauseInCombat && Condition[ConditionFlag.InCombat])
                return;

            var player = message.Sender.Payloads.OfType<PlayerPayload>().FirstOrDefault();
            var senderName = player?.PlayerName ?? StripGlyphs(message.Sender.TextValue);
            var me = PlayerState.CharacterName;
            var isOwn = type == XivChatType.TellOutgoing ||
                        message.SourceKind == XivChatRelationKind.LocalPlayer ||
                        (!string.IsNullOrEmpty(me) && senderName.EndsWith(me, StringComparison.Ordinal));
            if (isOwn && !cfg.TranslateOwnMessages)
                return;

            var plainText = message.Message.TextValue.Trim();
            if (plainText.Length > cfg.MaxMessageLength || OwnTagRegex().IsMatch(plainText))
                return;

            var text = MessageReader.Read(message.Message);
            if (MessageReader.IsNotWorthTranslating(text))
                return;

            if (cfg.PlayerWhitelist.Count > 0 &&
                !cfg.PlayerWhitelist.Any(p => senderName.Contains(p.Trim(), StringComparison.OrdinalIgnoreCase)))
                return;

            if (cfg.FilterMacroSpam && IsMacroSpam(senderName))
                return;

            var world = player?.World.ValueNullable?.Name.ToString();
            var reply = ReplyPrefixFor(type, senderName, world);
            var senderBytes = message.Sender.Encode();
            _ = HandleIncomingAsync(type, senderName, senderBytes, text, reply);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error while reading chat message");
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

    private async Task HandleIncomingAsync(XivChatType type, string senderName, byte[] senderBytes, string text, string? reply)
    {
        try
        {
            var result = Configuration.IncomingOnlyToPreferred
                ? await Translator.TranslateAsync(text, Languages.Get(Configuration.PreferredLanguage),
                                                  Configuration.TranslateOtherLanguages).ConfigureAwait(false)
                : await Translator.TranslateAsync(text, translateOthers: Configuration.TranslateOtherLanguages)
                                         .ConfigureAwait(false);
            if (result == null)
                return;

            AddHistory(new HistoryEntry(DateTime.Now, ChannelName(type), senderName, result, false, reply));

            var outType = Configuration.PrintInSameChannel ? type : Configuration.FallbackOutputChannel;
            var tagged = new SeStringBuilder()
                         .AddUiForeground($"[{result.Source.Code}>{result.Target.Code}] ", Configuration.TagColor)
                         .AddText(ChatSender.FoldForGame(result.Text))
                         .Build();

            await Framework.RunOnFrameworkThread(() =>
            {
                ChatGui.Print(new XivChatEntry
                {
                    Type = outType,
                    Name = SeString.Parse(senderBytes),
                    Message = tagged,
                });
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ReportError(ex, force: false);
        }
    }

    // ---------------------------------------------------------------- party finder

    private void OnContextMenuOpened(IMenuOpenedArgs args)
    {
        if (!Configuration.PartyFinderMenu || args.AddonName != "LookingForGroupDetail")
            return;
        args.AddMenuItem(new MenuItem
        {
            Name = "Translate description",
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
                ChatGui.Print("This listing has no description.", Tag);
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
            var result = await Translator.TranslateAsync(description, preferred, translateOthers: true).ConfigureAwait(false);
            if (result == null)
            {
                await Framework.RunOnFrameworkThread(() =>
                    ChatGui.Print($"The description is already in {preferred.Name}.", Tag));
                return;
            }

            AddHistory(new HistoryEntry(DateTime.Now, "Party Finder", "Listing", result, false, null));
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
        if (DateTime.Now - lastFallbackPrinted < TimeSpan.FromMinutes(5))
            return;
        lastFallbackPrinted = DateTime.Now;
        _ = Framework.RunOnFrameworkThread(() =>
            ChatGui.PrintError($"{reason}. Using Google until DeepL works again.", Tag));
    }

    private void OnDeepLKeyUnavailable(int index, string reason)
    {
        var next = index + 2 <= Translator.DeepL.Keys.Count ? $"trying key #{index + 2}" : "trying the remaining keys";
        _ = Framework.RunOnFrameworkThread(() =>
            ChatGui.Print($"DeepL key #{index + 1}: {reason}, {next}.", Tag));
    }

    private void ReportError(Exception ex, bool force)
    {
        var msg = ex is TranslationException or ArgumentException ? ex.Message : $"Unexpected error: {ex.Message}";
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
        >= XivChatType.Ls1 and <= XivChatType.Ls8 => $"LS{type - XivChatType.Ls1 + 1}",
        XivChatType.CrossLinkShell1 => "CWLS1",
        >= XivChatType.CrossLinkShell2 and <= XivChatType.CrossLinkShell8 => $"CWLS{type - XivChatType.CrossLinkShell2 + 2}",
        _ => type.ToString(),
    };
}
