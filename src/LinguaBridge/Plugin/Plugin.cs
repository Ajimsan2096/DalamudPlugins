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

    private readonly TranslationHistory history;
    private readonly ConversationLog conversations = new();
    private string? lastTellPartner;


    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        history = new TranslationHistory(() => Configuration.HistorySize);
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

    /// <summary>The /lb history, newest first (a copy).</summary>
    public IReadOnlyList<HistoryEntry> History => history.Snapshot();

    /// <summary>Changes whenever the history changes (to scroll to new messages).</summary>
    public long HistoryVersion => history.Version;

    public void ClearHistory() => history.Clear();

    private void AddHistory(HistoryEntry entry) => history.Add(entry);

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

}
