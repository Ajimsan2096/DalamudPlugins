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

/// <summary>Settings > Roleplay: RP mode, translation context, characters, long posts.</summary>
public sealed partial class ConfigWindow
{
    private void DrawRoleplay()
    {
        var cfg = plugin.Configuration;
        var changed = false;

        Ui.Section(L("Roleplay mode"), FontAwesomeIcon.TheaterMasks, first: true);
        var rp = cfg.RoleplayMode;
        if (ImGui.Checkbox(L("Roleplay mode"), ref rp))
            plugin.SetRoleplayMode(rp, announce: false);
        ImGui.SameLine();
        Ui.MutedText(L("(/lb rp toggles it)"));
        Ui.Hint(L("When on:\n" +
                          " - DeepL is told it is translating in-character prose (tone, voice, mature scenes untouched).\n" +
                          " - *actions*, \"speech\" / « speech », ((OOC)) and [OOC ...] keep their markers.\n" +
                          " - Emotes (/em) are translated, using the character name as subject; /tr /em works too.\n" +
                          " - Multi-line posts are not dropped as macro spam, and longer posts are read (see Long posts below).\n" +
                          " - IC, OOC, RP, ERP... and character names stay untouched."));

        Ui.Section(L("Translation context"), FontAwesomeIcon.Book);
        var f = Array.FindIndex(Formalities, x => x.Value == cfg.RpFormality);
        if (f < 0) f = 2;
        ImGui.SetNextItemWidth(Ui.S(200));
        if (ImGui.Combo(L("Formality in RP"), ref f, Formalities.Select(x => L(x.Label)).ToArray(), Formalities.Length))
        {
            cfg.RpFormality = Formalities[f].Value;
            plugin.Translator.ClearCache();
            changed = true;
        }
        ImGuiComponents.HelpMarker(L("\"Engine default\" lets DeepL pick tu/vous from the conversation context, which is " +
                                   "usually right in RP. Force \"vous\" for courtly / noble characters."));

        var lines = cfg.ContextLines;
        ImGui.SetNextItemWidth(Ui.S(200));
        if (ImGui.SliderInt(L("Conversation lines used as context"), ref lines, 0, 10))
            cfg.ContextLines = lines;
        changed |= Committed();
        ImGuiComponents.HelpMarker(L("The previous lines of the same chat (party, a tell, say + emotes...) are sent to DeepL " +
                                   "as context. DeepL does not bill context characters. It fixes pronouns, gender " +
                                   "agreement (il/elle, beau/belle), tu/vous and ambiguous words. Applies outside RP too."));

        var names = cfg.AutoProtectNames;
        if (ImGui.Checkbox(L("Never translate character names (sender and you)"), ref names))
        {
            cfg.AutoProtectNames = names;
            changed = true;
        }

        if (!sceneNotesLoaded)
        {
            sceneNotes = cfg.RpSceneNotes;
            sceneNotesLoaded = true;
        }
        ImGui.Dummy(Ui.S(0, 4));
        ImGui.TextUnformatted(L("Scene notes for DeepL (optional, not billed)"));
        ImGui.SameLine();
        ImGuiComponents.HelpMarker(L("Short facts that help the translation, in any language. Examples:\n" +
                                   "\"Setting: a quiet inn in Ul'dah at night. The characters are a couple, intimate and teasing. " +
                                   "Keep pet names (bebé, mon chéri, princess) as written.\""));
        if (ImGui.InputTextMultiline("##scene", ref sceneNotes, 600, new Vector2(-1, ImGui.GetTextLineHeight() * 4)))
            cfg.RpSceneNotes = sceneNotes;
        changed |= Committed();

        DrawCharacters(ref changed);

        Ui.Section(L("Long posts"), FontAwesomeIcon.PaperPlane);
        var rpMax = cfg.RoleplayMaxMessageLength;
        ImGui.SetNextItemWidth(Ui.S(200));
        if (ImGui.SliderInt(L("Longest post translated automatically in RP"), ref rpMax, 100, 1500))
            cfg.RoleplayMaxMessageLength = rpMax;
        changed |= Committed();
        ImGuiComponents.HelpMarker(L("Longer posts are listed in the /lb window with a \"Translate\" button."));

        var split = cfg.SplitLongMessages;
        if (ImGui.Checkbox(L("Split translations longer than the chat limit into several lines"), ref split))
        {
            cfg.SplitLongMessages = split;
            changed = true;
        }
        if (cfg.SplitLongMessages)
        {
            var markers = cfg.SplitMarkers;
            if (ImGui.Checkbox(L("Add (1/2), (2/2) markers"), ref markers))
            {
                cfg.SplitMarkers = markers;
                changed = true;
            }
        }

        ImGui.Dummy(Ui.S(0, 10));
        Ui.Callout(FontAwesomeIcon.ShieldAlt, Ui.Warning, L("Privacy"), L("Translated text is sent to DeepL or Google. According to DeepL, texts sent with the free API " +
                          "may be kept and used to improve their service, while DeepL Pro deletes them after translation. " +
                          "Google's free endpoint gives no guarantees. Keep that in mind for private or intimate scenes " +
                          "(you can pause translation with the LB switch or /lb off at any time)."));

        if (changed)
            plugin.SaveConfig();
    }

    private void DrawCharacters(ref bool changed)
    {
        var cfg = plugin.Configuration;
        Ui.Section(L("Characters"), FontAwesomeIcon.Book,
                   help: L("Gender and short notes per character. Sent to DeepL (not billed) only when that " +
                                   "character is in the conversation. Fixes il/elle, sur/sure, beau/belle, he/she, " +
                                   "and helps with tone (\"they are a couple, very affectionate\"). Works outside RP too."));

        var me = Plugin.PlayerState.CharacterName;
        var quick = false;
        if (!string.IsNullOrEmpty(me) && !cfg.Characters.Any(c => c.Name.Equals(me, StringComparison.OrdinalIgnoreCase)))
        {
            quick = true;
            if (Ui.Button(FontAwesomeIcon.Plus, LF("Add me ({0})", me)))
            {
                cfg.Characters.Add(new CharacterProfile { Name = me });
                changed = true;
            }
        }
        var last = plugin.History.FirstOrDefault(h => h.FromPlayer)?.Sender;
        if (last != null && !cfg.Characters.Any(c => c.Name.Equals(last, StringComparison.OrdinalIgnoreCase)))
        {
            if (quick)
                ImGui.SameLine();
            if (Ui.Button(FontAwesomeIcon.Plus, LF("Add {0}", last), tooltip: L("The last person who wrote to you")))
            {
                cfg.Characters.Add(new CharacterProfile { Name = last });
                changed = true;
            }
        }

        var remove = -1;
        using (var table = ImRaii.Table("chars", 4, ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp))
        {
            if (table)
            {
                ImGui.TableSetupColumn(L("Name"), ImGuiTableColumnFlags.WidthFixed, Ui.S(140));
                ImGui.TableSetupColumn(L("Gender"), ImGuiTableColumnFlags.WidthFixed, Ui.S(105));
                ImGui.TableSetupColumn(L("Notes"), ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFrameHeight());
                ImGui.TableHeadersRow();
                for (var i = 0; i < cfg.Characters.Count; i++)
                {
                    var c = cfg.Characters[i];
                    using var id = ImRaii.PushId(i);
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    var name = c.Name;
                    ImGui.SetNextItemWidth(-1);
                    if (ImGui.InputText("##n", ref name, 64))
                        c.Name = name;
                    changed |= Committed();
                    ImGui.TableNextColumn();
                    var g = Math.Max(0, Array.IndexOf(CharacterProfile.Genders, c.Gender));
                    ImGui.SetNextItemWidth(-1);
                    if (ImGui.Combo("##g", ref g, Array.ConvertAll(CharacterProfile.Genders, L), CharacterProfile.Genders.Length))
                    {
                        c.Gender = CharacterProfile.Genders[g];
                        changed = true;
                    }
                    ImGui.TableNextColumn();
                    var notes = c.Notes;
                    ImGui.SetNextItemWidth(-1);
                    if (ImGui.InputTextWithHint("##o", L("e.g. her boyfriend, affectionate, calls her 'princess'"), ref notes, 200))
                        c.Notes = notes;
                    changed |= Committed();
                    ImGui.TableNextColumn();
                    if (Ui.GhostIconButton("rm", FontAwesomeIcon.TrashAlt, LF("Remove {0}", c.Name), Ui.Danger))
                        remove = i;
                }
            }
        }
        if (Ui.Button(FontAwesomeIcon.Plus, L("Add character")))
        {
            cfg.Characters.Add(new CharacterProfile());
            changed = true;
        }
        if (remove >= 0)
        {
            cfg.Characters.RemoveAt(remove);
            changed = true;
        }
        if (changed)
            plugin.Translator.ClearCache();
    }
}
