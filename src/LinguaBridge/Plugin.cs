using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Dalamud.Game.Chat;
using Dalamud.Game.Command;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using LinguaBridge.Chat;
using LinguaBridge.Translation;
using LinguaBridge.Windows;

namespace LinguaBridge;

public sealed record HistoryEntry(DateTime Time, string Channel, string Sender, TranslationResult Translation, bool Outgoing);

public sealed partial class Plugin : IDalamudPlugin
{
    private const string MainCommand = "/linguabridge";
    private const string TranslateCommand = "/tr";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    public Configuration Configuration { get; }
    public TranslationService Translator { get; }

    private readonly WindowSystem windowSystem = new("LinguaBridge");
    private readonly MainWindow mainWindow;
    private readonly ConfigWindow configWindow;

    private readonly ConcurrentQueue<HistoryEntry> history = new();
    private DateTime lastErrorPrinted = DateTime.MinValue;

    /// <summary>Matches the tag we put in front of our own printed translations, to never re-translate them.</summary>
    [GeneratedRegex(@"^\[[A-Z?]{2}>[A-Z]{2}\] ", RegexOptions.CultureInvariant)]
    private static partial Regex OwnTagRegex();

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Translator = new TranslationService(() => Configuration);

        mainWindow = new MainWindow(this);
        configWindow = new ConfigWindow(this);
        windowSystem.AddWindow(mainWindow);
        windowSystem.AddWindow(configWindow);

        CommandManager.AddHandler(MainCommand, new CommandInfo(OnMainCommand)
        {
            HelpMessage = "Open the LinguaBridge window. \"/linguabridge config\" opens settings, \"/linguabridge on|off\" toggles chat translation.",
        });
        CommandManager.AddHandler(TranslateCommand, new CommandInfo(OnTranslateCommand)
        {
            HelpMessage = "Translate and send: /tr [:fr|:en|...] [/p|/fc|/t Name@World|...] message. " +
                          "Without a channel it goes to your current chat channel.",
        });

        PluginInterface.UiBuilder.Draw += windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        ChatGui.ChatMessage += OnChatMessage;
    }

    public void Dispose()
    {
        ChatGui.ChatMessage -= OnChatMessage;
        PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        CommandManager.RemoveHandler(MainCommand);
        CommandManager.RemoveHandler(TranslateCommand);
        windowSystem.RemoveAllWindows();
        Translator.Dispose();
    }

    public void SaveConfig() => PluginInterface.SavePluginConfig(Configuration);
    public void ToggleMainUi() => mainWindow.Toggle();
    public void ToggleConfigUi() => configWindow.Toggle();

    public IReadOnlyList<HistoryEntry> History => history.Reverse().ToList();
    public void ClearHistory() => history.Clear();

    private void AddHistory(HistoryEntry entry)
    {
        history.Enqueue(entry);
        while (history.Count > Math.Max(10, Configuration.HistorySize) && history.TryDequeue(out _)) { }
    }

    // ---------------------------------------------------------------- commands

    private void OnMainCommand(string command, string args)
    {
        switch (args.Trim().ToLowerInvariant())
        {
            case "config" or "settings":
                ToggleConfigUi();
                break;
            case "on":
                Configuration.IncomingEnabled = true;
                SaveConfig();
                ChatGui.Print("Chat translation enabled.", "LinguaBridge");
                break;
            case "off":
                Configuration.IncomingEnabled = false;
                SaveConfig();
                ChatGui.Print("Chat translation disabled.", "LinguaBridge");
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
            ChatGui.Print("Usage: /tr [:fr|:en] [/p|/fc|/t Name@World] message", "LinguaBridge");
            return;
        }

        var parsed = OutgoingCommand.Parse(args, out var error);
        if (parsed == null)
        {
            ChatGui.PrintError(error ?? "Could not parse the command.", "LinguaBridge");
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
                                  .AddText($"(original) {text}")
                                  .Build(),
                    });
            }).ConfigureAwait(false);

            if (result != null)
                AddHistory(new HistoryEntry(DateTime.Now, channelPrefix.Trim() is { Length: > 0 } p ? p : "current", "You", result, true));
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
            if (!cfg.IncomingEnabled)
                return;

            var type = message.LogKind;
            if (!cfg.Channels.Contains(type))
                return;

            var isOwn = type == XivChatType.TellOutgoing || message.SourceKind == XivChatRelationKind.LocalPlayer;
            if (isOwn && !cfg.TranslateOwnMessages)
                return;

            var text = message.Message.TextValue.Trim();
            if (text.Length < 2 || text.Length > cfg.MaxMessageLength)
                return;
            if (OwnTagRegex().IsMatch(text) || LanguageDetector.IsTrivial(text))
                return;

            var senderName = GetSenderName(message.Sender);
            if (cfg.PlayerWhitelist.Count > 0 &&
                !cfg.PlayerWhitelist.Any(p => senderName.Contains(p.Trim(), StringComparison.OrdinalIgnoreCase)))
                return;

            var senderBytes = message.Sender.Encode();
            _ = HandleIncomingAsync(type, senderName, senderBytes, text);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error while reading chat message");
        }
    }

    private async Task HandleIncomingAsync(XivChatType type, string senderName, byte[] senderBytes, string text)
    {
        try
        {
            var result = await Translator.TranslateAsync(text, translateOthers: Configuration.TranslateOtherLanguages)
                                         .ConfigureAwait(false);
            if (result == null)
                return;

            AddHistory(new HistoryEntry(DateTime.Now, ChannelName(type), senderName, result, false));

            var outType = Configuration.PrintInSameChannel ? type : Configuration.FallbackOutputChannel;
            var tagged = new SeStringBuilder()
                         .AddUiForeground($"[{result.Source.Code}>{result.Target.Code}] ", Configuration.TagColor)
                         .AddText(result.Text)
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
        _ = Framework.RunOnFrameworkThread(() => ChatGui.PrintError(msg, "LinguaBridge"));
    }

    private static string GetSenderName(SeString sender)
    {
        var player = sender.Payloads.OfType<PlayerPayload>().FirstOrDefault();
        if (player != null)
            return player.PlayerName;
        // Strip party-number / cross-world glyphs from the private-use area.
        return new string(sender.TextValue.Where(c => c is < '' or > '').ToArray()).Trim();
    }

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
