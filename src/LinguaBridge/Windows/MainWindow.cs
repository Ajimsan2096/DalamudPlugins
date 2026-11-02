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
    private int lastHistoryCount;

    public MainWindow(Plugin plugin) : base("LinguaBridge###LinguaBridgeMain")
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(440, 320), MaximumSize = new Vector2(4000, 4000) };
        Size = new Vector2(640, 520);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        DrawToolbar();
        ImGui.Separator();
        DrawHistory();
        ImGui.Separator();
        DrawCompose();
    }

    private void DrawToolbar()
    {
        var cfg = plugin.Configuration;
        var enabled = cfg.IncomingEnabled;
        if (ImGui.Checkbox("Translate chat", ref enabled))
            plugin.SetIncomingEnabled(enabled, announce: false);
        ImGui.SameLine();
        var rp = cfg.RoleplayMode;
        if (ImGui.Checkbox("RP", ref rp))
            plugin.SetRoleplayMode(rp, announce: false);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Roleplay mode: emotes, *actions*, \"speech\", ((OOC)), long posts. Settings > Roleplay.");
        ImGui.SameLine();
        ImGui.TextDisabled($"{cfg.LanguageA} <-> {cfg.LanguageB} via {cfg.Engine}");

        var buttons = ImGui.CalcTextSize("Settings").X + ImGui.CalcTextSize("Clear").X + ImGui.GetStyle().FramePadding.X * 4 +
                      ImGui.GetStyle().ItemSpacing.X + 4;
        ImGui.SameLine(ImGui.GetWindowContentRegionMax().X - buttons);
        if (ImGui.Button("Settings"))
            plugin.ToggleConfigUi();
        ImGui.SameLine();
        if (ImGui.Button("Clear"))
            plugin.ClearHistory();
    }

    private void DrawHistory()
    {
        var composeHeight = ImGui.GetFrameHeightWithSpacing() * 4 + ImGui.GetTextLineHeightWithSpacing() * 4 +
                            (plugin.Configuration.RoleplayMode ? ImGui.GetTextLineHeight() * 3 : 0);
        using var child = ImRaii.Child("history", new Vector2(0, -composeHeight), true);
        if (!child)
            return;

        var history = plugin.History;
        if (history.Count == 0)
        {
            ImGui.TextDisabled("Translations will appear here.");
            ImGui.TextDisabled("Tip: press \"Reply\" on a message to answer it translated, in the same channel.");
            return;
        }

        var i = 0;
        foreach (var e in history)
        {
            using var id = ImRaii.PushId(i++);
            var color = e.Outgoing ? new Vector4(0.55f, 0.85f, 1f, 1f) : new Vector4(1f, 0.85f, 0.45f, 1f);
            ImGui.TextColored(color, $"{e.Time:HH:mm} [{e.Channel}] {e.Sender}");
            ImGui.SameLine();
            ImGui.TextDisabled($"{e.Translation.Source.Code}>{e.Translation.Target.Code}");
            ImGui.SameLine();
            if (ImGui.SmallButton("Copy"))
                ImGui.SetClipboardText(e.Translation.Text);
            if (e.ReplyPrefix != null)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton("Reply"))
                {
                    replyPrefix = e.ReplyPrefix;
                    replyLabel = $"{e.Channel} - {e.Sender}";
                    focusInput = true;
                }
            }
            ImGui.TextWrapped(e.Translation.Text);
            using (ImRaii.PushColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]))
                ImGui.TextWrapped(e.Translation.Original);
            ImGui.Spacing();
        }

        // Newest is on top: jump there when something new arrives.
        if (history.Count != lastHistoryCount)
        {
            lastHistoryCount = history.Count;
            ImGui.SetScrollY(0);
        }
    }

    private void DrawCompose()
    {
        var cfg = plugin.Configuration;
        var a = Languages.Get(cfg.LanguageA);
        var b = Languages.Get(cfg.LanguageB);

        if (replyPrefix != null)
        {
            ImGui.TextColored(new Vector4(0.55f, 0.85f, 1f, 1f), $"Replying to: {replyLabel}");
            ImGui.SameLine();
            if (ImGui.SmallButton("Cancel reply"))
                replyPrefix = null;
        }
        else
        {
            ImGui.TextUnformatted("Write in any language; it is translated before sending. Enter = send.");
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
            ImGui.TextDisabled($"{System.Text.Encoding.UTF8.GetByteCount(input)} bytes (chat line limit 500, longer posts are split)");
        }
        else
        {
            ImGui.SetNextItemWidth(-1);
            submitted = ImGui.InputTextWithHint("##input", "Type your message...", ref input, 400, ImGuiInputTextFlags.EnterReturnsTrue);
        }
        if (submitted && !busy)
        {
            _ = TranslateAndSend();
            focusInput = true;
        }

        string[] targets = { "Auto (other language)", $"To {a.Name}", $"To {b.Name}" };
        var t = targetIndex + 1;
        ImGui.SetNextItemWidth(170);
        if (ImGui.Combo("##target", ref t, targets, targets.Length))
            targetIndex = t - 1;

        ImGui.SameLine();
        using (ImRaii.Disabled(replyPrefix != null))
        {
            ImGui.SetNextItemWidth(160);
            var channelLabels = Array.ConvertAll(SendChannels, c => c.Label);
            ImGui.Combo("##channel", ref channelIndex, channelLabels, channelLabels.Length);
        }

        ImGui.SameLine();
        using (ImRaii.Disabled(busy || string.IsNullOrWhiteSpace(input)))
        {
            if (ImGui.Button("Preview"))
                _ = Preview();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Translate without sending, and show a back-translation to check the meaning.");
            ImGui.SameLine();
            if (ImGui.Button("Send"))
                _ = TranslateAndSend();
        }

        if (preview.Length > 0)
        {
            ImGui.TextWrapped(preview);
            ImGui.SameLine();
            if (ImGui.SmallButton("Copy##preview"))
                ImGui.SetClipboardText(preview);
            if (backTranslation.Length > 0)
                ImGui.TextDisabled($"Means: {backTranslation}");
        }
        if (status.Length > 0)
            ImGui.TextDisabled(status);
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
        status = "Translating...";
        backTranslation = string.Empty;
        try
        {
            var r = await plugin.Translator.TranslateAsync(input.Trim(), SelectedTarget(), translateOthers: true);
            if (r == null)
            {
                preview = "(already in the target language)";
                status = string.Empty;
                return;
            }
            preview = ChatSender.Clean(r.Text);
            status = $"{r.Source.Code} > {r.Target.Code} ({r.Engine})";
            // Back-translate into the source language so the user can check the meaning.
            if (Languages.TryGet(r.Source.Code) is { } src)
            {
                var back = await plugin.Translator.TranslateAsync(r.Text, src, translateOthers: true);
                backTranslation = back?.Text ?? string.Empty;
            }
        }
        catch (Exception ex)
        {
            status = ex.Message;
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
        status = "Translating...";
        try
        {
            var prefix = replyPrefix ?? SendChannels[channelIndex].Prefix;
            if (await plugin.TranslateAndSendAsync(text, SelectedTarget(), prefix))
            {
                input = string.Empty;
                preview = string.Empty;
                backTranslation = string.Empty;
                status = "Sent.";
            }
            else
            {
                status = "Failed, see chat for details.";
            }
        }
        finally
        {
            busy = false;
        }
    }
}
