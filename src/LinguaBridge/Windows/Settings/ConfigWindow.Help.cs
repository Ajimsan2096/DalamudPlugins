using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using LinguaBridge.Translation;
using LinguaBridge.Tutorial;
using static LinguaBridge.Localization.Loc;

namespace LinguaBridge.Windows;

/// <summary>Settings > Help: getting started, commands, tips and tour.</summary>
public sealed partial class ConfigWindow
{
    private void DrawHelp()
    {
        Ui.Section(L("Getting started"), FontAwesomeIcon.Flag, first: true);
        if (Ui.Button(FontAwesomeIcon.Magic, L("Open setup wizard")))
            plugin.OpenWizard();
        DrawTutorialOptions();

        Ui.Section(L("How it works"), FontAwesomeIcon.InfoCircle);
        ImGui.TextWrapped(L("Incoming: messages in the selected channels are translated automatically and shown below the " +
                          "original with a tag like [EN>FR]. Only you see the translation."));
        ImGui.Dummy(Ui.S(0, 2));
        ImGui.TextWrapped(L("Outgoing: type /tr followed by your message. It is translated to the other language of the " +
                          "pair and sent to your current chat channel."));

        Ui.Section(L("Commands"), FontAwesomeIcon.Book);
        using (var table = ImRaii.Table("commands", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.PadOuterX))
        {
            if (table)
            {
                ImGui.TableSetupColumn(L("Command"), ImGuiTableColumnFlags.WidthFixed, Ui.S(220));
                ImGui.TableSetupColumn(L("What it does"), ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableHeadersRow();
                foreach (var (cmd, what) in Commands)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.TextColored(Ui.Accent, cmd);
                    if (ImGui.IsItemClicked())
                        ImGui.SetClipboardText(cmd);
                    Ui.Tooltip(L("Click to copy"));
                    ImGui.TableNextColumn();
                    ImGui.TextWrapped(L(what));
                }
            }
        }

        Ui.Section(L("Good to know"), FontAwesomeIcon.InfoCircle);
        Ui.Hint(L("- Party Finder: right-click a listing > \"Translate description\"."));
        Ui.Hint(L("- Translate only (nothing sent): in /lb pick \"Translate only\", or copy any text and type /lb clip. " +
                "The translation is copied, ready to paste in Aetherphone's ChocoChat or Velvet, Discord or a website."));
        Ui.Hint(L("- Aetherphone / Linkpearl: translations show up there too (as a second message). Use /tr /p ..., " +
                "/tr /t Name@World ... in its composer to send translated. See Chat > Aetherphone."));
        Ui.Hint(L("- Your friends need the plugin too to read your messages in their language, or simply use /tr so " +
                "they receive your text already translated."));
    }

    private void DrawTutorialOptions()
    {
        var cfg = plugin.Configuration;
        ImGui.Dummy(Ui.S(0, 4));
        using (ImRaii.Group())
        {
            ImGui.TextUnformatted(L("Guided tour and tips"));
            if (Ui.Button(FontAwesomeIcon.Flag, L("Tour of the main window")))
                plugin.Tutorial.StartTour(TourContent.MainTourId);
            ImGui.SameLine();
            if (Ui.Button(FontAwesomeIcon.Cog, L("Tour of the settings")))
                plugin.Tutorial.StartTour(TourContent.SettingsTourId);
            var tips = cfg.TipsEnabled;
            if (ImGui.Checkbox(L("Show tips the first time I use a feature"), ref tips))
                plugin.Tutorial.SetTipsEnabled(tips);
            ImGui.SameLine();
            if (Ui.Button(FontAwesomeIcon.Redo, L("Show all tips again"),
                          tooltip: L("Every tip and tour offer will appear again when it makes sense.")))
                plugin.Tutorial.ResetTips();
            Ui.MutedText(L("Also: /lb tour, /lb tour settings, /lb tips on|off|reset"));
            var still = cfg.ReduceMotion;
            if (ImGui.Checkbox(L("Reduce motion (no spinner or pulsing highlight)"), ref still))
            {
                cfg.ReduceMotion = still;
                plugin.SaveConfig();
            }
        }
        plugin.Tutorial.Anchor("config.help.tutorial");
    }
}
