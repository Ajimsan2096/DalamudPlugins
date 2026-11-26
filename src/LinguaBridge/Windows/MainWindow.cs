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
public sealed class MainWindow : Window
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

    // ---------------------------------------------------------------- history

    private void DrawHistory(IReadOnlyList<HistoryEntry> history)
    {
        if (history.Count > 0)
        {
            Ui.Icon(FontAwesomeIcon.Search, Ui.Muted);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(Ui.S(220));
            ImGui.InputTextWithHint("##hfilter", L("Filter by player or text..."), ref historyFilter, 60);
            if (historyFilter.Length > 0)
            {
                ImGui.SameLine();
                if (Ui.GhostIconButton("clearfilter", FontAwesomeIcon.Times, L("Clear the filter")))
                    historyFilter = string.Empty;
            }
        }
        var reserve = composeHeight > 0 ? composeHeight : ImGui.GetFrameHeightWithSpacing() * 5;
        using (ImRaii.PushColor(ImGuiCol.ChildBg, ImGui.GetStyle().Colors[(int)ImGuiCol.FrameBg] with { W = 0.35f }))
        using (ImRaii.PushStyle(ImGuiStyleVar.ChildRounding, Ui.Rounding))
        using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Ui.S(8, 8)))
        {
            using var child = ImRaii.Child("history", new Vector2(0, -reserve), false, ImGuiWindowFlags.AlwaysUseWindowPadding);
            if (!child)
                return;
            plugin.Tutorial.AnchorWindow("main.history");

            if (history.Count == 0)
            {
                DrawEmptyHistory();
                return;
            }

            // Newest is on top: jump there when something new arrives, unless you scrolled down to read.
            var version = plugin.HistoryVersion;
            if (version != lastHistoryVersion)
            {
                if (ImGui.GetScrollY() <= Ui.S(4))
                    ImGui.SetScrollY(0);
                lastHistoryVersion = version;
            }

            var i = 0;
            var shown = 0;
            foreach (var e in history)
            {
                if (historyFilter.Length > 0 && !Matches(e, historyFilter))
                    continue;
                using var id = ImRaii.PushId(i++);
                DrawEntry(e);
                ImGui.Dummy(Ui.S(0, 4));
                shown++;
            }
            if (shown == 0)
                Ui.MutedText(L("No message matches the filter."));
        }
    }

    private static bool Matches(HistoryEntry e, string filter) =>
        e.Sender.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
        e.Translation.Text.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
        e.Translation.Original.Contains(filter, StringComparison.OrdinalIgnoreCase);

    /// <summary>Player names are shown as they are; "You", "Clipboard", "Listing" are translated.</summary>
    private static string SenderLabel(HistoryEntry e) => e.FromPlayer ? e.Sender : L(e.Sender);

    private static void DrawEmptyHistory()
    {
        var avail = ImGui.GetContentRegionAvail();
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + Math.Max(0, avail.Y / 2 - ImGui.GetTextLineHeight() * 3));
        using var font = ImRaii.PushFont(UiBuilder.IconFont);
        Centered(FontAwesomeIcon.Comments.ToIconString(), Ui.Muted);
        font.Dispose();
        ImGui.Spacing();
        Centered(L("No translations yet"), ImGui.GetStyle().Colors[(int)ImGuiCol.Text]);
        Centered(L("Messages from the chats you selected will appear here."), Ui.Muted);
        Centered(L("Press Reply on one to answer it translated, in the same channel."), Ui.Muted);
    }

    private static void Centered(string text, Vector4 color)
    {
        var w = ImGui.CalcTextSize(text).X;
        ImGui.SetCursorPosX(Math.Max(ImGui.GetCursorPosX(), (ImGui.GetWindowWidth() - w) / 2));
        ImGui.TextColored(color, text);
    }

    /// <summary>One message as a card: who / where / when, the translation, then the original in muted text.</summary>
    private void DrawEntry(HistoryEntry e)
    {
        var dl = ImGui.GetWindowDrawList();
        var start = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var pad = Ui.S(10, 6);
        var edge = e.Outgoing ? Ui.Accent : Ui.Incoming;

        dl.ChannelsSplit(2);
        dl.ChannelsSetCurrent(1);
        ImGui.SetCursorScreenPos(start + pad);
        using (ImRaii.Group())
        {
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width - pad.X * 2);

            // Header: direction icon, sender, channel, language pair ... time and actions on the right.
            Ui.Icon(e.Outgoing ? FontAwesomeIcon.ArrowUp : FontAwesomeIcon.ArrowDown, edge);
            Ui.Tooltip(e.Outgoing ? L("Sent by you") : L("Received"));
            ImGui.SameLine();
            ImGui.TextColored(edge, SenderLabel(e));
            ImGui.SameLine();
            Ui.Badge(L(e.Channel), Ui.Muted);
            ImGui.SameLine();
            if (e.Skipped)
                Ui.Badge(L("not translated"), Ui.Warning, L("Longer than the maximum message length (Settings > Chat / Roleplay)."));
            else
                Ui.Badge($"{e.Translation.Source.Code} > {e.Translation.Target.Code}", edge, // loc-ok: codes / numbers only
                         LF("{0} to {1} ({2})", L(e.Translation.Source.Name), L(e.Translation.Target.Name), e.Translation.Engine));

            var time = $"{e.Time:HH:mm}";
            var spacing = ImGui.GetStyle().ItemSpacing.X;
            var actions = ImGui.CalcTextSize(time).X + spacing + Ui.IconButtonWidth(FontAwesomeIcon.Copy) +
                          (e.ReplyPrefix != null ? spacing + Ui.IconButtonWidth(FontAwesomeIcon.Reply) : 0);
            ImGui.SameLine();
            var x = start.X - ImGui.GetWindowPos().X + width - pad.X - actions;
            if (x > ImGui.GetCursorPosX())
                ImGui.SetCursorPosX(x);
            var y = ImGui.GetCursorPosY();
            ImGui.SetCursorPosY(y - ImGui.GetStyle().FramePadding.Y);
            using (ImRaii.Group())
            {
                ImGui.AlignTextToFramePadding();
                ImGui.TextColored(Ui.Muted, time);
                ImGui.SameLine();
                if (Ui.GhostIconButton("copy", FontAwesomeIcon.Copy, L("Copy the translation")))
                {
                    ImGui.SetClipboardText(e.Translation.Text);
                    SetStatus(L("Translation copied to the clipboard."), StatusKind.Success);
                }
                if (e.ReplyPrefix != null)
                {
                    ImGui.SameLine();
                    if (Ui.GhostIconButton("reply", FontAwesomeIcon.Reply, LF("Reply to {0} translated, in {1}", SenderLabel(e), L(e.Channel))))
                    {
                        replyPrefix = e.ReplyPrefix;
                        replyLabel = $"{SenderLabel(e)} ({L(e.Channel)})";
                        focusInput = true;
                        plugin.Tutorial.ShowTip(TourContent.FirstReply);
                    }
                }
            }

            // Body.
            ImGui.Dummy(Ui.S(0, 1));
            if (e.Skipped)
            {
                ImGui.TextWrapped(e.Translation.Text);
                using (ImRaii.Disabled(busy))
                {
                    if (Ui.Button(FontAwesomeIcon.Language, L("Translate")))
                        _ = TranslateSkipped(e);
                }
                ImGui.PopTextWrapPos();
                goto done;
            }
            ImGui.TextWrapped(e.Translation.Text);
            if (e.Learning is { } learning)
            {
                using (ImRaii.PushColor(ImGuiCol.Text, Ui.Learning))
                    ImGui.TextWrapped($"[{learning.Target.Code}] {learning.Text}");
                if (ImGui.IsItemClicked())
                {
                    ImGui.SetClipboardText(learning.Text);
                    SetStatus(L("Learning line copied to the clipboard."), StatusKind.Success);
                }
                Ui.Tooltip(LF("Learning language ({0}). Click to copy.", L(learning.Target.Name)));
            }
            using (ImRaii.PushColor(ImGuiCol.Text, Ui.Muted))
                ImGui.TextWrapped(e.Translation.Original);
            Ui.Tooltip(L("Original message"));

            ImGui.PopTextWrapPos();
            done: ;
        }
        var end = new Vector2(start.X + width, ImGui.GetItemRectMax().Y + pad.Y);

        dl.ChannelsSetCurrent(0);
        var hovered = ImGui.IsMouseHoveringRect(start, end) && ImGui.IsWindowHovered();
        var bg = ImGui.GetStyle().Colors[(int)ImGuiCol.WindowBg];
        dl.AddRectFilled(start, end, ImGui.GetColorU32(hovered ? edge with { W = 0.08f } : bg with { W = 0.55f }), Ui.Rounding);
        dl.AddRectFilled(start, new Vector2(start.X + Ui.S(3), end.Y), ImGui.GetColorU32(edge with { W = 0.85f }), Ui.Rounding,
                         ImDrawFlags.RoundCornersLeft);
        dl.ChannelsMerge();

        ImGui.SetCursorScreenPos(start);
        ImGui.Dummy(end - start);
    }

    // ---------------------------------------------------------------- compose

    private void DrawCompose()
    {
        var cfg = plugin.Configuration;
        var a = Languages.Get(cfg.LanguageA);
        var b = Languages.Get(cfg.LanguageB);
        ImGui.Dummy(Ui.S(0, 4));

        if (replyPrefix != null)
        {
            using (ImRaii.Group())
            {
                Ui.Icon(FontAwesomeIcon.Reply, Ui.Accent);
                ImGui.SameLine();
                ImGui.AlignTextToFramePadding();
                ImGui.TextColored(Ui.Accent, LF("Replying to {0}", replyLabel));
                ImGui.SameLine();
                if (Ui.GhostIconButton("cancelreply", FontAwesomeIcon.Times, L("Cancel the reply (back to the channel list)"), Ui.Danger))
                    replyPrefix = null;
            }
        }
        else
        {
            var mode = cfg.ComposeTranslateOnly ? 1 : 0;
            if (Ui.Segmented("mode", new[] { L("Send to chat"), L("Translate only") }, ref mode, new[]
                {
                    L("What you type is translated, then sent to the game chat."),
                    L("Nothing is sent: the translation is copied so you can paste it anywhere\n" +
                    "(Aetherphone's ChocoChat or Velvet, Discord, a website...).\n" +
                    "Also: /lb clip translates what you copied."),
                }))
            {
                cfg.ComposeTranslateOnly = mode == 1;
                plugin.SaveConfig();
                if (mode == 1)
                    plugin.Tutorial.ShowTip(TourContent.FirstTranslateOnly);
            }
            plugin.Tutorial.Anchor("main.mode");
            ImGui.SameLine(0, Ui.S(10));
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(Ui.Muted, cfg.ComposeTranslateOnly ? L("Enter: translate and copy") : L("Enter: translate and send"));
        }

        if (focusInput)
        {
            ImGui.SetKeyboardFocusHere();
            focusInput = false;
        }
        bool submitted;
        if (cfg.RoleplayMode)
        {
            // Bigger box for RP posts; long translations are split into several chat lines automatically.
            submitted = ImGui.InputTextMultiline("##input", ref input, 2000,
                                                 new Vector2(-1, ImGui.GetTextLineHeight() * 4.5f),
                                                 ImGuiInputTextFlags.EnterReturnsTrue);
            plugin.Tutorial.Anchor("main.input");
            var bytes = System.Text.Encoding.UTF8.GetByteCount(input);
            ImGui.TextColored(bytes > 500 ? Ui.Warning : Ui.Muted,
                              bytes > 500 ? LF("{0} bytes: longer than one chat line (500), it will be split", bytes)
                                          : LF("{0} / 500 bytes per chat line", bytes));
        }
        else
        {
            ImGui.SetNextItemWidth(-1);
            submitted = ImGui.InputTextWithHint("##input", L("Type your message in any language..."), ref input, 400,
                                                ImGuiInputTextFlags.EnterReturnsTrue);
            plugin.Tutorial.Anchor("main.input");
        }
        var translateOnly = cfg.ComposeTranslateOnly && replyPrefix == null;
        if (submitted && !busy)
        {
            _ = translateOnly ? TranslateAndCopy() : TranslateAndSend();
            focusInput = true;
        }

        // Options on the left, actions on the right.
        // Target: auto, the pair first, then every other language (to write to someone outside the pair).
        var codes = new[] { (string?)null, a.Code, b.Code }
                    .Concat(Languages.All.Where(l => l.Code != a.Code && l.Code != b.Code).Select(l => (string?)l.Code))
                    .ToArray();
        var targets = codes.Select(c => c == null ? L("Auto (other language)") : LF("To {0}", L(Languages.Get(c).Name))).ToArray();
        var t = Math.Max(0, Array.IndexOf(codes, targetCode));
        Ui.Icon(FontAwesomeIcon.Language, Ui.Muted);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(Ui.S(165));
        if (ImGui.Combo("##target", ref t, targets, targets.Length))
            targetCode = codes[t];
        Ui.Tooltip(L("Language to translate into"));
        plugin.Tutorial.Anchor("main.target");

        var empty = string.IsNullOrWhiteSpace(input);
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        if (translateOnly)
        {
            var w = Ui.IconTextButtonWidth(FontAwesomeIcon.Paste, L("Paste & translate")) +
                    Ui.IconTextButtonWidth(FontAwesomeIcon.Copy, L("Translate & copy")) + spacing;
            Ui.AlignRight(w);
            using (ImRaii.Disabled(busy))
            {
                if (Ui.Button(FontAwesomeIcon.Paste, L("Paste & translate"),
                              tooltip: L("Translates what you copied (Ctrl+C) and copies the translation back.")))
                {
                    var clip = Clipboard.GetText();
                    if (string.IsNullOrWhiteSpace(clip))
                    {
                        SetStatus(L("The clipboard has no text."), StatusKind.Warning);
                    }
                    else
                    {
                        input = clip.Trim();
                        _ = TranslateAndCopy();
                    }
                }
            }
            ImGui.SameLine();
            using (ImRaii.Disabled(busy || empty))
            {
                if (Ui.Button(FontAwesomeIcon.Copy, L("Translate & copy"), primary: true,
                              tooltip: empty ? L("Type something first.") : null))
                    _ = TranslateAndCopy();
            }
            plugin.Tutorial.Anchor("main.preview");
            DrawPreview(canCheckMeaning: false);
            return;
        }

        ImGui.SameLine(0, Ui.S(10));
        using (ImRaii.Disabled(replyPrefix != null))
        {
            Ui.Icon(FontAwesomeIcon.Comments, Ui.Muted);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(Ui.S(160));
            var channelLabels = Array.ConvertAll(ChannelLabels, L);
            ImGui.Combo("##channel", ref channelIndex, channelLabels, channelLabels.Length);
        }
        Ui.Tooltip(replyPrefix != null ? L("Replying: the message goes where the original came from.") : L("Chat channel to send to"));
        plugin.Tutorial.Anchor("main.channel");

        var needsName = replyPrefix == null && SendChannels[channelIndex].Prefix == TellToName;
        if (needsName)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(Ui.S(170));
            ImGui.InputTextWithHint("##tellname", L("First Last@World"), ref tellName, 64);
            Ui.Tooltip(L("Who receives the tell: First Last@World (the world can be left out on your own world)."));
        }

        var noName = needsName && tellName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < 2;
        var buttons = Ui.IconTextButtonWidth(FontAwesomeIcon.Eye, L("Preview")) +
                      Ui.IconTextButtonWidth(FontAwesomeIcon.PaperPlane, L("Send")) + spacing;
        Ui.AlignRight(buttons);
        using (ImRaii.Disabled(busy || empty || noName))
        {
            if (Ui.Button(FontAwesomeIcon.Eye, L("Preview"),
                          tooltip: noName ? L("Type the name of the player first (First Last@World).")
                                          : L("Translate without sending, to check it first. Send then sends exactly this.")))
                _ = Preview();
            plugin.Tutorial.Anchor("main.preview");
            ImGui.SameLine();
            if (Ui.Button(FontAwesomeIcon.PaperPlane, L("Send"), primary: true,
                          tooltip: empty ? L("Type something first.")
                                 : noName ? L("Type the name of the player first (First Last@World).")
                                 : L("Translate and send (Enter)")))
                _ = TranslateAndSend();
        }

        DrawPreview(canCheckMeaning: true);
    }

    private void DrawPreview(bool canCheckMeaning)
    {
        if (preview.Length > 0)
        {
            ImGui.Dummy(Ui.S(0, 2));
            Ui.Callout(FontAwesomeIcon.Eye, Ui.Accent, null, () =>
            {
                ImGui.TextWrapped(preview);
                if (backTranslation.Length > 0)
                {
                    using (ImRaii.PushColor(ImGuiCol.Text, Ui.Muted))
                        ImGui.TextWrapped(LF("Means: {0}", backTranslation));
                    Ui.Tooltip(L("Back-translation into your language, to check the meaning."));
                }
                if (ImGui.SmallButton(L("Copy") + "##preview"))
                {
                    ImGui.SetClipboardText(preview);
                    SetStatus(L("Translation copied to the clipboard."), StatusKind.Success);
                }
                if (canCheckMeaning && backTranslation.Length == 0 && draft?.Result != null)
                {
                    ImGui.SameLine();
                    using (ImRaii.Disabled(checkingMeaning))
                    {
                        if (ImGui.SmallButton(L("Check the meaning") + "##preview"))
                            _ = CheckMeaning();
                    }
                    Ui.Tooltip(L("Translates it back into your language (one more request)."));
                }
                ImGui.SameLine();
                if (ImGui.SmallButton(L("Dismiss") + "##preview"))
                {
                    preview = string.Empty;
                    backTranslation = string.Empty;
                    draft = null;
                }
            });
        }
        if (status.Length > 0)
        {
            ImGui.Dummy(Ui.S(0, 1));
            Ui.Status(status, statusKind);
        }
    }

    private Language? SelectedTarget() => targetCode == null ? null : Languages.TryGet(targetCode);

    /// <summary>Chat prefix the message goes to: the reply target, the tell name, or the selected channel.</summary>
    private string CurrentPrefix()
    {
        if (replyPrefix != null)
            return replyPrefix;
        var prefix = SendChannels[channelIndex].Prefix;
        return prefix == TellToName ? $"/t {tellName.Trim()} " : prefix;
    }

    private async Task Preview()
    {
        busy = true;
        SetStatus(L("Translating..."), StatusKind.Busy);
        backTranslation = string.Empty;
        draft = null;
        try
        {
            var d = await plugin.PrepareOutgoingAsync(input.Trim(), SelectedTarget(), CurrentPrefix());
            if (d.Result == null)
            {
                preview = string.Empty;
                SetStatus(L("Already in the target language: nothing to translate."), StatusKind.Info);
                return;
            }
            draft = d;
            preview = d.Body;
            SetStatus($"{L(d.Result.Source.Name)} > {L(d.Result.Target.Name)} ({d.Result.Engine})", StatusKind.Info); // loc-ok: codes / numbers only
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, StatusKind.Error);
        }
        finally
        {
            busy = false;
            previewed = true;
        }
    }

    /// <summary>Back-translation of the preview, only when asked (it costs one more request).</summary>
    private async Task CheckMeaning()
    {
        var d = draft;
        if (d?.Result == null || Languages.TryGet(d.Result.Source.Code) is not { } src)
            return;
        checkingMeaning = true;
        try
        {
            var back = await plugin.Translator.TranslateAsync(d.Body, src);
            backTranslation = back?.Text ?? d.Original;
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, StatusKind.Error);
        }
        finally
        {
            checkingMeaning = false;
        }
    }

    private async Task TranslateSkipped(HistoryEntry e)
    {
        busy = true;
        SetStatus(L("Translating..."), StatusKind.Busy);
        try
        {
            SetStatus(await plugin.TranslateSkippedAsync(e)
                          ? L("Translated.")
                          : L("Already in the target language: nothing to translate."),
                      StatusKind.Info);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, StatusKind.Error);
        }
        finally
        {
            busy = false;
        }
    }

    /// <summary>Translate-only mode: translates the box and copies the result; nothing is sent.</summary>
    private async Task TranslateAndCopy()
    {
        var text = input.Trim();
        if (text.Length == 0)
            return;
        busy = true;
        SetStatus(L("Translating..."), StatusKind.Busy);
        backTranslation = string.Empty;
        draft = null;
        try
        {
            var r = await plugin.TranslateTextAsync(text, SelectedTarget());
            if (r == null)
            {
                preview = string.Empty;
                SetStatus(L("Already in the target language: nothing to translate."), StatusKind.Info);
                return;
            }
            preview = r.Text;
            plugin.CopyToClipboard(r.Text);
            SetStatus(LF("{0} > {1} ({2}), copied to the clipboard.", L(r.Source.Name), L(r.Target.Name), r.Engine), StatusKind.Success);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, StatusKind.Error);
        }
        finally
        {
            busy = false;
        }
    }

    private async Task TranslateAndSend()
    {
        var text = input.Trim();
        if (text.Length == 0)
            return;
        busy = true;
        SetStatus(L("Translating..."), StatusKind.Busy);
        try
        {
            var prefix = CurrentPrefix();
            var target = SelectedTarget();
            // The previewed translation is sent as it is, as long as the text, language and channel did not change.
            var d = draft;
            var sent = d != null && d.Original == text && d.Prefix == prefix && d.Target?.Code == target?.Code
                ? await plugin.SendDraftAsync(d)
                : await plugin.TranslateAndSendAsync(text, target, prefix);
            if (sent)
            {
                input = string.Empty;
                preview = string.Empty;
                backTranslation = string.Empty;
                draft = null;
                SetStatus(L("Sent."), StatusKind.Success);
            }
            else
            {
                SetStatus(L("Could not send. See the chat log for details."), StatusKind.Error);
            }
        }
        finally
        {
            busy = false;
        }
    }
}
