using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using LinguaBridge.Chat;
using LinguaBridge.Translation;

namespace LinguaBridge.Windows;

/// <summary>Translation history plus a box to compose, preview and send translated messages.</summary>
public sealed class MainWindow : Window
{
    private static readonly (string Label, string Prefix)[] SendChannels =
    {
        ("Current channel", ""), ("Say", "/s "), ("Party", "/p "), ("Alliance", "/a "), ("Free Company", "/fc "),
        ("Reply (last tell)", "/r "), ("Linkshell 1", "/l1 "), ("Linkshell 2", "/l2 "), ("Cross-world LS 1", "/cwl1 "),
        ("Cross-world LS 2", "/cwl2 "), ("Shout", "/sh "), ("Yell", "/y "),
    };

    private readonly Plugin plugin;
    private string input = string.Empty;
    private string preview = string.Empty;
    private int targetIndex = -1; // -1 = auto (other language of the pair)
    private int channelIndex;
    private bool busy;
    private string status = string.Empty;

    public MainWindow(Plugin plugin) : base("LinguaBridge###LinguaBridgeMain")
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(420, 300), MaximumSize = new Vector2(4000, 4000) };
        Size = new Vector2(620, 480);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        var cfg = plugin.Configuration;

        var enabled = cfg.IncomingEnabled;
        if (ImGui.Checkbox("Translate incoming chat", ref enabled))
        {
            cfg.IncomingEnabled = enabled;
            plugin.SaveConfig();
        }
        ImGui.SameLine();
        ImGui.TextDisabled($"{cfg.LanguageA} <-> {cfg.LanguageB} via {cfg.Engine}");
        ImGui.SameLine(ImGui.GetContentRegionAvail().X - 120);
        if (ImGui.Button("Settings"))
            plugin.ToggleConfigUi();
        ImGui.SameLine();
        if (ImGui.Button("Clear"))
            plugin.ClearHistory();

        ImGui.Separator();
        DrawHistory();
        ImGui.Separator();
        DrawCompose();
    }

    private void DrawHistory()
    {
        var composeHeight = ImGui.GetFrameHeightWithSpacing() * 5 + ImGui.GetTextLineHeightWithSpacing() * 2;
        using var child = ImRaii.Child("history", new Vector2(0, -composeHeight), true);
        if (!child)
            return;

        var history = plugin.History;
        if (history.Count == 0)
        {
            ImGui.TextDisabled("Translations will appear here.");
            return;
        }

        var i = 0;
        foreach (var e in history)
        {
            using var id = ImRaii.PushId(i++);
            var color = e.Outgoing ? new Vector4(0.55f, 0.85f, 1f, 1f) : new Vector4(1f, 0.85f, 0.45f, 1f);
            ImGui.TextColored(color, $"{e.Time:HH:mm} [{e.Channel}] {e.Sender}  {e.Translation.Source.Code}>{e.Translation.Target.Code}");
            ImGui.SameLine();
            if (ImGui.SmallButton("Copy"))
                ImGui.SetClipboardText(e.Translation.Text);
            ImGui.TextWrapped(e.Translation.Text);
            ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
            ImGui.TextWrapped(e.Translation.Original);
            ImGui.PopStyleColor();
            ImGui.Spacing();
        }
    }

    private void DrawCompose()
    {
        ImGui.TextUnformatted("Write a message (it will be translated before sending):");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##input", ref input, 400, ImGuiInputTextFlags.EnterReturnsTrue) && !busy)
            _ = TranslateAndSend();

        var cfg = plugin.Configuration;
        var a = Languages.Get(cfg.LanguageA);
        var b = Languages.Get(cfg.LanguageB);
        string[] targets = { "Auto (other language)", $"To {a.Name}", $"To {b.Name}" };
        var t = targetIndex + 1;
        ImGui.SetNextItemWidth(180);
        if (ImGui.Combo("##target", ref t, targets, targets.Length))
            targetIndex = t - 1;

        ImGui.SameLine();
        ImGui.SetNextItemWidth(160);
        var channelLabels = Array.ConvertAll(SendChannels, c => c.Label);
        ImGui.Combo("##channel", ref channelIndex, channelLabels, channelLabels.Length);

        ImGui.SameLine();
        using (ImRaii.Disabled(busy || string.IsNullOrWhiteSpace(input)))
        {
            if (ImGui.Button("Preview"))
                _ = Preview();
            ImGui.SameLine();
            if (ImGui.Button("Translate & Send"))
                _ = TranslateAndSend();
        }

        if (preview.Length > 0)
        {
            ImGui.TextWrapped(preview);
            ImGui.SameLine();
            if (ImGui.SmallButton("Copy##preview"))
                ImGui.SetClipboardText(preview);
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

    private async System.Threading.Tasks.Task Preview()
    {
        busy = true;
        status = "Translating...";
        try
        {
            var r = await plugin.Translator.TranslateAsync(input.Trim(), SelectedTarget(), translateOthers: true);
            preview = r == null ? "(already in the target language)" : ChatSender.Clean(r.Text);
            status = r == null ? string.Empty : $"{r.Source.Code} > {r.Target.Code} ({r.Engine})";
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

    private async System.Threading.Tasks.Task TranslateAndSend()
    {
        var text = input.Trim();
        if (text.Length == 0)
            return;
        busy = true;
        status = "Translating...";
        try
        {
            if (await plugin.TranslateAndSendAsync(text, SelectedTarget(), SendChannels[channelIndex].Prefix))
            {
                input = string.Empty;
                preview = string.Empty;
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
