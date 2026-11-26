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

/// <summary>Settings > Chat: channels, filters, output, Aetherphone, extras.</summary>
public sealed partial class ConfigWindow
{
    private void DrawChat()
    {
        var cfg = plugin.Configuration;
        var changed = false;

        Ui.Section(L("Incoming chat"), FontAwesomeIcon.Comments, first: true);
        var enabled = cfg.IncomingEnabled;
        if (ImGui.Checkbox(L("Translate incoming chat"), ref enabled))
        {
            cfg.IncomingEnabled = enabled;
            changed = true;
        }

        Ui.Section(L("Channels to translate"), FontAwesomeIcon.Comments);
        using (var table = ImRaii.Table("channels", 2, ImGuiTableFlags.SizingStretchSame))
        {
            foreach (var (label, types) in table ? ChannelGroups : Array.Empty<(string, XivChatType[])>())
            {
                ImGui.TableNextColumn();
                var on = types.All(cfg.Channels.Contains);
                if (ImGui.Checkbox(L(label), ref on))
                {
                    foreach (var t in types)
                    {
                        if (on) cfg.Channels.Add(t);
                        else cfg.Channels.Remove(t);
                    }
                    changed = true;
                }
            }
        }

        Ui.Section(L("Filters"), FontAwesomeIcon.Search);
        var own = cfg.TranslateOwnMessages;
        if (ImGui.Checkbox(L("Translate my own messages too"), ref own))
        {
            cfg.TranslateOwnMessages = own;
            changed = true;
        }

        var combat = cfg.PauseInCombat;
        if (ImGui.Checkbox(L("Pause while in combat"), ref combat))
        {
            cfg.PauseInCombat = combat;
            changed = true;
        }

        var spam = cfg.FilterMacroSpam;
        if (ImGui.Checkbox(L("Ignore macro spam"), ref spam))
        {
            cfg.FilterMacroSpam = spam;
            changed = true;
        }
        ImGuiComponents.HelpMarker(L("Skips a player's lines when they arrive less than 0.65 s apart (multi-line macros), " +
                                   "saving DeepL characters."));

        ImGui.TextUnformatted(L("Only translate these players (comma separated, empty = everyone):"));
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##whitelist", ref whitelistText, 1000))
            cfg.PlayerWhitelist = SplitList(whitelistText);
        changed |= Committed();

        var max = cfg.MaxMessageLength;
        ImGui.SetNextItemWidth(Ui.S(150));
        if (ImGui.SliderInt(L("Max message length"), ref max, 20, 500))
            cfg.MaxMessageLength = max;
        changed |= Committed();
        ImGuiComponents.HelpMarker(L("Longer messages are not translated automatically: they are listed in the /lb window with a " +
                                   "\"Translate\" button. In roleplay mode the limit is set in Settings > Roleplay."));

        Ui.Section(L("Output"), FontAwesomeIcon.Eye);

        string[] displays = { L("Translation below the original"), L("Replace the original with its translation"),
                              L("Replace only in roleplay mode") };
        var display = (int)cfg.IncomingDisplay;
        ImGui.SetNextItemWidth(Ui.S(300));
        if (ImGui.Combo(L("How to show translations"), ref display, displays, displays.Length))
        {
            cfg.IncomingDisplay = (IncomingDisplay)display;
            changed = true;
        }
        ImGuiComponents.HelpMarker(L("Replace: you only see the translated message (with its [FR>EN] tag), in the same order, " +
                                   "so long RP posts are not shown twice. If a translation fails or takes over 12 s, " +
                                   "the original is shown instead. Originals stay in the /lb history window."));

        var same = cfg.PrintInSameChannel;
        if (ImGui.Checkbox(L("Show translation in the same channel as the original"), ref same))
        {
            cfg.PrintInSameChannel = same;
            changed = true;
        }
        if (!cfg.PrintInSameChannel)
        {
            var idx = Math.Max(0, Array.FindIndex(OutputChannels, c => c.Type == cfg.FallbackOutputChannel));
            ImGui.SetNextItemWidth(Ui.S(150));
            if (ImGui.Combo(L("Output channel"), ref idx, OutputChannels.Select(c => L(c.Label)).ToArray(), OutputChannels.Length))
            {
                cfg.FallbackOutputChannel = OutputChannels[idx].Type;
                changed = true;
            }
        }
        else if (cfg.IncomingDisplay != IncomingDisplay.Replace)
        {
            var tellsEcho = cfg.TellsToEcho;
            if (ImGui.Checkbox(L("Show translations of tells in Echo"), ref tellsEcho))
            {
                cfg.TellsToEcho = tellsEcho;
                changed = true;
            }
            ImGuiComponents.HelpMarker(L("Otherwise the translation shows up as a second tell from that player, and other plugins " +
                                       "(notification sounds, Discord bridges, chat plugins) treat it as a new tell."));
        }

        var color = cfg.TagColor;
        if (ColorPicker.Draw("tagcolor", L("Tag colour"), ref color))
        {
            cfg.TagColor = color;
            changed = true;
        }

        var historySize = cfg.HistorySize;
        ImGui.SetNextItemWidth(Ui.S(150));
        if (ImGui.SliderInt(L("Messages kept in the /lb history"), ref historySize, 50, 1000))
            cfg.HistorySize = historySize;
        changed |= Committed();

        var shortTag = cfg.ShortTag;
        if (ImGui.Checkbox(L("Short tag: [FR] instead of [EN>FR]"), ref shortTag))
        {
            cfg.ShortTag = shortTag;
            changed = true;
        }
        ImGuiComponents.HelpMarker(L("Takes less room, handy in narrow chat bubbles such as Aetherphone's Linkpearl."));

        var echo = cfg.EchoOriginalOnSend;
        if (ImGui.Checkbox(L("After /tr, show my original text in Echo"), ref echo))
        {
            cfg.EchoOriginalOnSend = echo;
            changed = true;
        }

        DrawAetherphone(ref changed);

        Ui.Section(L("Extras"), FontAwesomeIcon.Magic);

        var dtr = cfg.ShowDtrEntry;
        if (ImGui.Checkbox(L("Show \"LB\" on/off switch in the server info bar"), ref dtr))
        {
            cfg.ShowDtrEntry = dtr;
            changed = true;
        }
        ImGuiComponents.HelpMarker(L("Top right of the screen. Left click toggles translation, right click opens the window."));

        var pf = cfg.PartyFinderMenu;
        if (ImGui.Checkbox(L("Party Finder: \"Translate description\" in the right-click menu"), ref pf))
        {
            cfg.PartyFinderMenu = pf;
            changed = true;
        }

        if (changed)
            plugin.SaveConfig();
    }

    private void DrawAetherphone(ref bool changed)
    {
        var cfg = plugin.Configuration;
        var detected = plugin.AetherphoneLoaded;
        Ui.Section(L("Aetherphone (Linkpearl)"), FontAwesomeIcon.Comments);
        if (detected)
        {
            ImGui.SameLine();
            Ui.Badge(L("detected"), Ui.Success);
        }

        var handled = cfg.TranslateHandledMessages;
        if (ImGui.Checkbox(L("Translate messages other plugins hid from the chat log"), ref handled))
        {
            cfg.TranslateHandledMessages = handled;
            changed = true;
        }
        ImGuiComponents.HelpMarker(L("Turn this on if a Linkpearl channel is set to \"hide from game chat\": otherwise " +
                                   "those messages are never translated. The translation is printed in the chat log " +
                                   "(that is how it reaches Linkpearl), the original stays hidden.\n" +
                                   "Careful: spam filters hide messages too, and their translations would show up."));

        if (!detected)
            return;
        using (ImRaii.PushColor(ImGuiCol.Text, Ui.Muted))
        {
            ImGui.TextWrapped(
                L("- Linkpearl shows the translations as a second message from the same player, also in Replace mode " +
                "(Aetherphone keeps the original). \"Below the original\" + short tag reads best there.\n" +
                "- Send translated from Linkpearl's composer with /tr and the channel: /tr /p hello, " +
                "/tr /t Name@World hello (without a channel it goes to the game's current channel, not the Linkpearl tab).\n" +
                "- ChocoChat and Velvet are not game chat: use the main window in \"Translate only\" or /lb clip."));
        }
    }
}
