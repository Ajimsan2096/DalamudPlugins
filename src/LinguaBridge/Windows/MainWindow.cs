using System;
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

namespace LinguaBridge.Windows;

/// <summary>Translation history with one-click replies, plus a box to compose, preview and send.</summary>
public sealed class MainWindow : Window
{
    private static readonly (string Label, string Prefix)[] SendChannels =
    {
        ("Current channel", ""), ("Say", "/s "), ("Emote (/em)", "/em "), ("Party", "/p "), ("Alliance", "/a "), ("Free Company", "/fc "),
        ("Reply (last tell)", "/r "), ("Linkshell 1", "/l1 "), ("Linkshell 2", "/l2 "), ("Linkshell 3", "/l3 "),
        ("Cross-world LS 1", "/cwl1 "), ("Cross-world LS 2", "/cwl2 "), ("Cross-world LS 3", "/cwl3 "),
        ("Shout", "/sh "), ("Yell", "/y "), ("Echo (test, only you)", "/e "),
    };

    private static readonly string[] ChannelLabels = Array.ConvertAll(SendChannels, c => c.Label);

    private readonly Plugin plugin;
    private string input = string.Empty;
    private string preview = string.Empty;
    private string backTranslation = string.Empty;
    private int targetIndex = -1; // -1 = auto (other language of the pair)
    private int channelIndex;
    private string? replyPrefix;   // set by a "Reply" button; overrides the channel combo
    private string replyLabel = string.Empty;
    private bool focusInput;
    private bool busy;
    private string status = string.Empty;
    private StatusKind statusKind;
    private int lastHistoryCount;
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
        DrawToolbar();
        ImGui.Dummy(Ui.S(0, 2));
        DrawHistory();

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

    private void DrawToolbar()
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
            ImGui.TextColored(enabled ? Ui.Success : Ui.Muted, enabled ? "Translating" : "Paused");
            if (ImGui.IsItemClicked())
                plugin.SetIncomingEnabled(!enabled, announce: false);
        }
        Ui.Tooltip("Translate incoming chat automatically (also: the \"LB\" entry in the server info bar, /lb on|off).");
        plugin.Tutorial.Anchor("main.toggle");

        ImGui.SameLine(0, Ui.S(16));
        using (ImRaii.Group())
        {
            var rp = cfg.RoleplayMode;
            if (ImGuiComponents.ToggleButton("##rp", ref rp))
                plugin.SetRoleplayMode(rp, announce: false);
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(rp ? Ui.Accent : Ui.Muted, "Roleplay");
            if (ImGui.IsItemClicked())
                plugin.SetRoleplayMode(!rp, announce: false);
        }
        Ui.Tooltip("Roleplay mode: emotes, *actions*, \"speech\", ((OOC)), long posts. Settings > Roleplay.");
        plugin.Tutorial.Anchor("main.rp");

        ImGui.SameLine(0, Ui.S(16));
        var pad = (ImGui.GetFrameHeight() - ImGui.GetTextLineHeight() - Ui.S(2)) / 2;
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + pad);
        Ui.Badge($"{cfg.LanguageA}  <>  {cfg.LanguageB}   ·   {cfg.Engine}", Ui.Accent,
                 $"Translating between {Languages.Get(cfg.LanguageA).Name} and {Languages.Get(cfg.LanguageB).Name} " +
                 $"with {cfg.Engine}.\nChange it in Settings > Languages / Engine.");
        plugin.Tutorial.Anchor("main.pair");

        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var right = Ui.IconButtonWidth(FontAwesomeIcon.Broom) + Ui.IconButtonWidth(FontAwesomeIcon.QuestionCircle) +
                    Ui.IconButtonWidth(FontAwesomeIcon.Cog) + spacing * 2;
        Ui.AlignRight(right);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - pad);
        using (ImRaii.Disabled(plugin.History.Count == 0))
        {
            if (Ui.IconButton("clear", FontAwesomeIcon.Broom, "Clear the history"))
                ImGui.OpenPopup("##confirmclear");
        }
        DrawClearConfirm();
        ImGui.SameLine();
        if (Ui.IconButton("tour", FontAwesomeIcon.QuestionCircle, "Guided tour of this window (/lb tour)"))
            plugin.Tutorial.StartTour(TourContent.MainTourId);
        ImGui.SameLine();
        if (Ui.IconButton("settings", FontAwesomeIcon.Cog, "Settings (/lb config)"))
            plugin.ToggleConfigUi();
        plugin.Tutorial.Anchor("main.settings");
    }

    /// <summary>Clearing cannot be undone, so it asks first.</summary>
    private void DrawClearConfirm()
    {
        using var popup = ImRaii.Popup("##confirmclear");
        if (!popup)
            return;
        ImGui.TextUnformatted($"Clear all {plugin.History.Count} translations from the history?");
        ImGui.TextColored(Ui.Muted, "Your chat log is not affected.");
        ImGui.Spacing();
        if (Ui.Button(FontAwesomeIcon.Broom, "Clear history", primary: true))
        {
            plugin.ClearHistory();
            ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel") || ImGui.IsKeyPressed(ImGuiKey.Escape))
            ImGui.CloseCurrentPopup();
    }

    // ---------------------------------------------------------------- history

    private void DrawHistory()
    {
        var reserve = composeHeight > 0 ? composeHeight : ImGui.GetFrameHeightWithSpacing() * 5;
        using (ImRaii.PushColor(ImGuiCol.ChildBg, ImGui.GetStyle().Colors[(int)ImGuiCol.FrameBg] with { W = 0.35f }))
        using (ImRaii.PushStyle(ImGuiStyleVar.ChildRounding, Ui.Rounding))
        using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Ui.S(8, 8)))
        {
            using var child = ImRaii.Child("history", new Vector2(0, -reserve), false, ImGuiWindowFlags.AlwaysUseWindowPadding);
            if (!child)
                return;
            plugin.Tutorial.AnchorWindow("main.history");

            var history = plugin.History;
            if (history.Count == 0)
            {
                DrawEmptyHistory();
                return;
            }

            var i = 0;
            foreach (var e in history)
            {
                using var id = ImRaii.PushId(i++);
                DrawEntry(e);
                ImGui.Dummy(Ui.S(0, 4));
            }

            // Newest is on top: jump there when something new arrives.
            if (history.Count != lastHistoryCount)
            {
                lastHistoryCount = history.Count;
                ImGui.SetScrollY(0);
            }
        }
    }

    private static void DrawEmptyHistory()
    {
        var avail = ImGui.GetContentRegionAvail();
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + Math.Max(0, avail.Y / 2 - ImGui.GetTextLineHeight() * 3));
        using var font = ImRaii.PushFont(UiBuilder.IconFont);
        Centered(FontAwesomeIcon.Comments.ToIconString(), Ui.Muted);
        font.Dispose();
        ImGui.Spacing();
        Centered("No translations yet", ImGui.GetStyle().Colors[(int)ImGuiCol.Text]);
        Centered("Messages from the chats you selected will appear here.", Ui.Muted);
        Centered("Press Reply on one to answer it translated, in the same channel.", Ui.Muted);
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
            Ui.Tooltip(e.Outgoing ? "Sent by you" : "Received");
            ImGui.SameLine();
            ImGui.TextColored(edge, e.Sender);
            ImGui.SameLine();
            Ui.Badge(e.Channel, Ui.Muted);
            ImGui.SameLine();
            Ui.Badge($"{e.Translation.Source.Code} > {e.Translation.Target.Code}", edge,
                     $"{e.Translation.Source.Name} to {e.Translation.Target.Name} ({e.Translation.Engine})");

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
                if (Ui.GhostIconButton("copy", FontAwesomeIcon.Copy, "Copy the translation"))
                {
                    ImGui.SetClipboardText(e.Translation.Text);
                    SetStatus("Translation copied to the clipboard.", StatusKind.Success);
                }
                if (e.ReplyPrefix != null)
                {
                    ImGui.SameLine();
                    if (Ui.GhostIconButton("reply", FontAwesomeIcon.Reply, $"Reply to {e.Sender} translated, in {e.Channel}"))
                    {
                        replyPrefix = e.ReplyPrefix;
                        replyLabel = $"{e.Sender} ({e.Channel})";
                        focusInput = true;
                        plugin.Tutorial.ShowTip(TourContent.FirstReply);
                    }
                }
            }

            // Body.
            ImGui.Dummy(Ui.S(0, 1));
            ImGui.TextWrapped(e.Translation.Text);
            if (e.Learning is { } learning)
            {
                using (ImRaii.PushColor(ImGuiCol.Text, Ui.Learning))
                    ImGui.TextWrapped($"[{learning.Target.Code}] {learning.Text}");
                if (ImGui.IsItemClicked())
                {
                    ImGui.SetClipboardText(learning.Text);
                    SetStatus("Learning line copied to the clipboard.", StatusKind.Success);
                }
                Ui.Tooltip($"Learning language ({learning.Target.Name}). Click to copy.");
            }
            using (ImRaii.PushColor(ImGuiCol.Text, Ui.Muted))
                ImGui.TextWrapped(e.Translation.Original);
            Ui.Tooltip("Original message");

            ImGui.PopTextWrapPos();
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
                ImGui.TextColored(Ui.Accent, $"Replying to {replyLabel}");
                ImGui.SameLine();
                if (Ui.GhostIconButton("cancelreply", FontAwesomeIcon.Times, "Cancel the reply (back to the channel list)", Ui.Danger))
                    replyPrefix = null;
            }
        }
        else
        {
            var mode = cfg.ComposeTranslateOnly ? 1 : 0;
            if (Ui.Segmented("mode", new[] { "Send to chat", "Translate only" }, ref mode, new[]
                {
                    "What you type is translated, then sent to the game chat.",
                    "Nothing is sent: the translation is copied so you can paste it anywhere\n" +
                    "(Aetherphone's ChocoChat or Velvet, Discord, a website...).\n" +
                    "Also: /lb clip translates what you copied.",
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
            ImGui.TextColored(Ui.Muted, cfg.ComposeTranslateOnly ? "Enter: translate and copy" : "Enter: translate and send");
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
                              bytes > 500 ? $"{bytes} bytes: longer than one chat line (500), it will be split"
                                          : $"{bytes} / 500 bytes per chat line");
        }
        else
        {
            ImGui.SetNextItemWidth(-1);
            submitted = ImGui.InputTextWithHint("##input", "Type your message in any language...", ref input, 400,
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
        string[] targets = { "Auto (other language)", $"To {a.Name}", $"To {b.Name}" };
        var t = targetIndex + 1;
        Ui.Icon(FontAwesomeIcon.Language, Ui.Muted);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(Ui.S(165));
        if (ImGui.Combo("##target", ref t, targets, targets.Length))
            targetIndex = t - 1;
        Ui.Tooltip("Language to translate into");
        plugin.Tutorial.Anchor("main.target");

        var empty = string.IsNullOrWhiteSpace(input);
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        if (translateOnly)
        {
            var w = Ui.IconTextButtonWidth(FontAwesomeIcon.Paste, "Paste & translate") +
                    Ui.IconTextButtonWidth(FontAwesomeIcon.Copy, "Translate & copy") + spacing;
            Ui.AlignRight(w);
            using (ImRaii.Disabled(busy))
            {
                if (Ui.Button(FontAwesomeIcon.Paste, "Paste & translate",
                              tooltip: "Translates what you copied (Ctrl+C) and copies the translation back."))
                {
                    var clip = Clipboard.GetText();
                    if (string.IsNullOrWhiteSpace(clip))
                    {
                        SetStatus("The clipboard has no text.", StatusKind.Warning);
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
                if (Ui.Button(FontAwesomeIcon.Copy, "Translate & copy", primary: true,
                              tooltip: empty ? "Type something first." : null))
                    _ = TranslateAndCopy();
            }
            plugin.Tutorial.Anchor("main.preview");
            DrawPreview();
            return;
        }

        ImGui.SameLine(0, Ui.S(10));
        using (ImRaii.Disabled(replyPrefix != null))
        {
            Ui.Icon(FontAwesomeIcon.Comments, Ui.Muted);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(Ui.S(160));
            ImGui.Combo("##channel", ref channelIndex, ChannelLabels, ChannelLabels.Length);
        }
        Ui.Tooltip(replyPrefix != null ? "Replying: the message goes where the original came from." : "Chat channel to send to");
        plugin.Tutorial.Anchor("main.channel");

        var buttons = Ui.IconTextButtonWidth(FontAwesomeIcon.Eye, "Preview") +
                      Ui.IconTextButtonWidth(FontAwesomeIcon.PaperPlane, "Send") + spacing;
        Ui.AlignRight(buttons);
        using (ImRaii.Disabled(busy || empty))
        {
            if (Ui.Button(FontAwesomeIcon.Eye, "Preview",
                          tooltip: "Translate without sending, and show a back-translation to check the meaning."))
                _ = Preview();
            plugin.Tutorial.Anchor("main.preview");
            ImGui.SameLine();
            if (Ui.Button(FontAwesomeIcon.PaperPlane, "Send", primary: true,
                          tooltip: empty ? "Type something first." : "Translate and send (Enter)"))
                _ = TranslateAndSend();
        }

        DrawPreview();
    }

    private void DrawPreview()
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
                        ImGui.TextWrapped($"Means: {backTranslation}");
                    Ui.Tooltip("Back-translation into your language, to check the meaning.");
                }
                if (ImGui.SmallButton("Copy##preview"))
                {
                    ImGui.SetClipboardText(preview);
                    SetStatus("Translation copied to the clipboard.", StatusKind.Success);
                }
                ImGui.SameLine();
                if (ImGui.SmallButton("Dismiss##preview"))
                {
                    preview = string.Empty;
                    backTranslation = string.Empty;
                }
            });
        }
        if (status.Length > 0)
        {
            ImGui.Dummy(Ui.S(0, 1));
            Ui.Status(status, statusKind);
        }
    }

    private Language? SelectedTarget()
    {
        var cfg = plugin.Configuration;
        return targetIndex switch
        {
            0 => Languages.Get(cfg.LanguageA),
            1 => Languages.Get(cfg.LanguageB),
            _ => null,
        };
    }

    private async Task Preview()
    {
        busy = true;
        SetStatus("Translating...", StatusKind.Busy);
        backTranslation = string.Empty;
        try
        {
            var r = await plugin.Translator.TranslateAsync(input.Trim(), SelectedTarget(), translateOthers: true);
            if (r == null)
            {
                preview = string.Empty;
                SetStatus("Already in the target language: nothing to translate.", StatusKind.Info);
                return;
            }
            preview = ChatSender.Clean(r.Text);
            SetStatus($"{r.Source.Name} > {r.Target.Name} ({r.Engine})", StatusKind.Info);
            // Back-translate into the source language so the user can check the meaning.
            if (Languages.TryGet(r.Source.Code) is { } src)
            {
                var back = await plugin.Translator.TranslateAsync(r.Text, src, translateOthers: true);
                backTranslation = back?.Text ?? string.Empty;
            }
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

    /// <summary>Translate-only mode: translates the box and copies the result; nothing is sent.</summary>
    private async Task TranslateAndCopy()
    {
        var text = input.Trim();
        if (text.Length == 0)
            return;
        busy = true;
        SetStatus("Translating...", StatusKind.Busy);
        backTranslation = string.Empty;
        try
        {
            var r = await plugin.TranslateTextAsync(text, SelectedTarget());
            if (r == null)
            {
                preview = string.Empty;
                SetStatus("Already in the target language: nothing to translate.", StatusKind.Info);
                return;
            }
            preview = r.Text;
            plugin.CopyToClipboard(r.Text);
            SetStatus($"{r.Source.Name} > {r.Target.Name} ({r.Engine}), copied to the clipboard.", StatusKind.Success);
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
        SetStatus("Translating...", StatusKind.Busy);
        try
        {
            var prefix = replyPrefix ?? SendChannels[channelIndex].Prefix;
            if (await plugin.TranslateAndSendAsync(text, SelectedTarget(), prefix))
            {
                input = string.Empty;
                preview = string.Empty;
                backTranslation = string.Empty;
                SetStatus("Sent.", StatusKind.Success);
            }
            else
            {
                SetStatus("Could not send. See the chat log for details.", StatusKind.Error);
            }
        }
        finally
        {
            busy = false;
        }
    }
}
