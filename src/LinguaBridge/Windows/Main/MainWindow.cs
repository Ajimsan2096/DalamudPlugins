using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using LinguaBridge.Chat;
using LinguaBridge.Translation;
using LinguaBridge.Tutorial;
using static LinguaBridge.Localization.Loc;

namespace LinguaBridge.Windows;

/// <summary>Translation history with one-click replies, plus a box to compose, preview and send.</summary>
public sealed partial class MainWindow : Window
{
    /// <summary>Prefix meaning "a tell to the name typed next to the channel list".</summary>
    private const string TellToName = "/t {name} ";

    private static readonly (string Label, string Prefix)[] SendChannels =
    {
        ("Current channel", ""), ("Say", "/s "), ("Emote (/em)", "/em "), ("Party", "/p "), ("Alliance", "/a "), ("Free Company", "/fc "),
        ("Reply (last tell)", "/r "), ("Tell to...", TellToName),
        ("Linkshell 1", "/l1 "), ("Linkshell 2", "/l2 "), ("Linkshell 3", "/l3 "), ("Linkshell 4", "/l4 "),
        ("Linkshell 5", "/l5 "), ("Linkshell 6", "/l6 "), ("Linkshell 7", "/l7 "), ("Linkshell 8", "/l8 "),
        ("Cross-world LS 1", "/cwl1 "), ("Cross-world LS 2", "/cwl2 "), ("Cross-world LS 3", "/cwl3 "), ("Cross-world LS 4", "/cwl4 "),
        ("Cross-world LS 5", "/cwl5 "), ("Cross-world LS 6", "/cwl6 "), ("Cross-world LS 7", "/cwl7 "), ("Cross-world LS 8", "/cwl8 "),
        ("Novice Network", "/n "), ("PvP Team", "/pvpteam "),
        ("Shout", "/sh "), ("Yell", "/y "), ("Echo (test, only you)", "/e "),
    };

    private static readonly string[] ChannelLabels = Array.ConvertAll(SendChannels, c => c.Label);

    private readonly Plugin plugin;
    private string input = string.Empty;
    private string preview = string.Empty;
    private string backTranslation = string.Empty;
    private string? targetCode;    // null = auto (other language of the pair)
    private int channelIndex;
    private string tellName = string.Empty;
    private string historyFilter = string.Empty;
    private OutgoingDraft? draft;  // what Preview showed: Send sends exactly this while nothing changed
    private bool checkingMeaning;
    private string? replyPrefix;   // set by a "Reply" button; overrides the channel combo
    private string replyLabel = string.Empty;
    private bool focusInput;
    private bool busy;
    private string status = string.Empty;
    private StatusKind statusKind;
    private long lastHistoryVersion;
    private bool previewed;        // a preview finished (for the guided tour)
    private float composeHeight;   // measured every frame, so the history fills exactly the rest

    public MainWindow(Plugin plugin) : base("LinguaBridge###LinguaBridgeMain")
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(460, 340), MaximumSize = new Vector2(4000, 4000) };
        Size = new Vector2(640, 560);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    internal bool HasInput => input.Trim().Length > 0;
    internal bool HasPreview => preview.Length > 0 || previewed;

    public override void OnOpen() => plugin.Tutorial.ShowTip(TourContent.OfferMainTour);

    public override void Draw()
    {
        plugin.Tutorial.AnchorWindow("main.window");
        var history = plugin.History; // one copy per frame
        DrawToolbar(history);
        ImGui.Dummy(Ui.S(0, 2));
        DrawHistory(history);

        var top = ImGui.GetCursorPosY();
        DrawCompose();
        composeHeight = ImGui.GetCursorPosY() - top;
    }

    private void SetStatus(string text, StatusKind kind)
    {
        status = text;
        statusKind = kind;
    }

    // ---------------------------------------------------------------- toolbar

    private void DrawToolbar(IReadOnlyList<HistoryEntry> history)
    {
        var cfg = plugin.Configuration;

        // Master switch: a toggle plus a word that says the state (not colour alone).
        using (ImRaii.Group())
        {
            var enabled = cfg.IncomingEnabled;
            if (ImGuiComponents.ToggleButton("##translate", ref enabled))
                plugin.SetIncomingEnabled(enabled, announce: false);
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(enabled ? Ui.Success : Ui.Muted, enabled ? L("Translating") : L("Paused"));
            if (ImGui.IsItemClicked())
                plugin.SetIncomingEnabled(!enabled, announce: false);
        }
        Ui.Tooltip(L("Translate incoming chat automatically (also: the \"LB\" entry in the server info bar, /lb on|off)."));
        plugin.Tutorial.Anchor("main.toggle");

        ImGui.SameLine(0, Ui.S(16));
        using (ImRaii.Group())
        {
            var rp = cfg.RoleplayMode;
            if (ImGuiComponents.ToggleButton("##rp", ref rp))
                plugin.SetRoleplayMode(rp, announce: false);
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(rp ? Ui.Accent : Ui.Muted, L("Roleplay"));
            if (ImGui.IsItemClicked())
                plugin.SetRoleplayMode(!rp, announce: false);
        }
        Ui.Tooltip(L("Roleplay mode: emotes, *actions*, \"speech\", ((OOC)), long posts. Settings > Roleplay."));
        plugin.Tutorial.Anchor("main.rp");

        ImGui.SameLine(0, Ui.S(16));
        var pad = (ImGui.GetFrameHeight() - ImGui.GetTextLineHeight() - Ui.S(2)) / 2;
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + pad);
        var engine = plugin.Translator.ActiveEngineName;
        var googlePause = engine == "Google" ? plugin.Translator.Google.PauseRemaining : null;
        var engineLabel = engine == "AI" ? L("AI") : engine;
        var pausedTag = googlePause != null ? " " + L("(paused)") : string.Empty;
        Ui.Badge($"{cfg.LanguageA}  <>  {cfg.LanguageB}   ·   {engineLabel}{pausedTag}", // loc-ok: codes / numbers only
                 googlePause != null ? Ui.Warning : Ui.Accent,
                 LF("Translating between {0} and {1} with {2}.\nChange it in Settings > Languages / Engine.",
                    L(Languages.Get(cfg.LanguageA).Name), L(Languages.Get(cfg.LanguageB).Name), engineLabel) +
                 (googlePause is { } left
                     ? "\n\n" + LF("Google paused (too many requests): resumes by itself at {0:HH:mm}.", DateTime.Now + left)
                     : string.Empty));
        plugin.Tutorial.Anchor("main.pair");

        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var right = Ui.IconButtonWidth(FontAwesomeIcon.Broom) + Ui.IconButtonWidth(FontAwesomeIcon.QuestionCircle) +
                    Ui.IconButtonWidth(FontAwesomeIcon.Cog) + spacing * 2;
        Ui.AlignRight(right);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - pad);
        using (ImRaii.Disabled(history.Count == 0))
        {
            if (Ui.IconButton("clear", FontAwesomeIcon.Broom, L("Clear the history")))
                ImGui.OpenPopup("##confirmclear");
        }
        DrawClearConfirm(history.Count);
        ImGui.SameLine();
        if (Ui.IconButton("tour", FontAwesomeIcon.QuestionCircle, L("Guided tour of this window (/lb tour)")))
            plugin.Tutorial.StartTour(TourContent.MainTourId);
        ImGui.SameLine();
        if (Ui.IconButton("settings", FontAwesomeIcon.Cog, L("Settings (/lb config)")))
            plugin.ToggleConfigUi();
        plugin.Tutorial.Anchor("main.settings");
    }

    /// <summary>Clearing cannot be undone, so it asks first.</summary>
    private void DrawClearConfirm(int count)
    {
        using var popup = ImRaii.Popup("##confirmclear");
        if (!popup)
            return;
        ImGui.TextUnformatted(LF("Clear all {0} translations from the history?", count));
        ImGui.TextColored(Ui.Muted, L("Your chat log is not affected."));
        ImGui.Spacing();
        if (Ui.Button(FontAwesomeIcon.Broom, L("Clear history"), primary: true))
        {
            plugin.ClearHistory();
            ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine();
        if (ImGui.Button(L("Cancel")) || ImGui.IsKeyPressed(ImGuiKey.Escape))
            ImGui.CloseCurrentPopup();
    }
}
