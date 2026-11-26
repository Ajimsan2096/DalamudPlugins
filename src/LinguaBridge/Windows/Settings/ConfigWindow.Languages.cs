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

/// <summary>Settings > Languages: interface language, language pair, reading, languages to translate, learning.</summary>
public sealed partial class ConfigWindow
{
    private void DrawLanguages()
    {
        var cfg = plugin.Configuration;
        var names = Languages.All.Select(l => $"{L(l.Name)} ({l.Code})").ToArray();
        var changed = false;

        Ui.Section(L("Plugin language"), FontAwesomeIcon.Language, first: true,
                   help: L("Language of these menus, tips and messages. It does not change the languages your chat is translated between."));
        var ui = Localization.Loc.Current;
        if (SetupWizard.LanguageButtons(ref ui))
            plugin.SetUiLanguage(ui);

        Ui.Section(L("Language pair"), FontAwesomeIcon.ExchangeAlt);
        var a = IndexOf(cfg.LanguageA);
        ImGui.SetNextItemWidth(Ui.FieldWidth);
        if (ImGui.Combo(L("Language A"), ref a, names, names.Length))
        {
            // Picking the other language swaps the two (A and B can never be the same).
            if (Languages.All[a].Code == cfg.LanguageB)
                cfg.LanguageB = cfg.LanguageA;
            cfg.LanguageA = Languages.All[a].Code;
            changed = true;
        }

        var b = IndexOf(cfg.LanguageB);
        ImGui.SetNextItemWidth(Ui.FieldWidth);
        if (ImGui.Combo(L("Language B"), ref b, names, names.Length))
        {
            if (Languages.All[b].Code == cfg.LanguageA)
                cfg.LanguageA = cfg.LanguageB;
            cfg.LanguageB = Languages.All[b].Code;
            changed = true;
        }
        Ui.Hint(L("Your messages sent with /tr go from A to B or from B to A automatically."));

        Ui.Section(L("Reading"), FontAwesomeIcon.Book);
        var p = IndexOf(cfg.PreferredLanguage);
        ImGui.SetNextItemWidth(Ui.FieldWidth);
        if (ImGui.Combo(L("Language you read best"), ref p, names, names.Length))
        {
            cfg.PreferredLanguage = Languages.All[p].Code;
            changed = true;
        }
        ImGuiComponents.HelpMarker(L("Incoming chat is translated into this language, plus Party Finder descriptions."));

        var oneWay = cfg.IncomingOnlyToPreferred;
        if (ImGui.Checkbox(L("Only translate incoming chat into the language I read best"), ref oneWay))
        {
            cfg.IncomingOnlyToPreferred = oneWay;
            changed = true;
        }
        ImGuiComponents.HelpMarker(L("On: messages already in your language are left alone (e.g. reading English, only French " +
                                   "gets translated).\nOff: both directions (English is also translated to French). " +
                                   "/tr always works both ways."));

        var expand = cfg.ExpandAbbreviations;
        if (ImGui.Checkbox(L("Expand chat abbreviations before translating (jsp, tkt, idk, tbh...)"), ref expand))
        {
            cfg.ExpandAbbreviations = expand;
            changed = true;
        }

        DrawLanguageFilter(ref changed);

        Ui.Section(L("Never translate these words"), FontAwesomeIcon.ShieldAlt);
        Ui.MutedText(L("Comma separated"));
        ImGui.SameLine();
        ImGuiComponents.HelpMarker(L("Character names, nicknames, FC / static names, inside jokes... DeepL keeps them exactly " +
                                   "as written. Item links and auto-translate phrases are always kept automatically."));
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputTextWithHint("##protected", L("e.g. Rukenu, Les Chocobos, Moogle"), ref protectedText, 2000))
            cfg.ProtectedTerms = SplitList(protectedText);
        changed |= Committed();

        Ui.Section(L("Learning a language"), FontAwesomeIcon.GraduationCap,
                   help: L("Adds a second line under each translated message, in the language you are learning.\n" +
                                   "Example: you read Spanish, a German friend writes in German. You get the Spanish " +
                                   "translation as usual, plus the same message in English, to pick it up little by little.\n" +
                                   "Each learning line is one more request to the engine (DeepL characters)."));
        var learningNames = new[] { L("Off") }.Concat(names).ToArray();
        var l = string.IsNullOrWhiteSpace(cfg.LearningLanguage) || Languages.TryGet(cfg.LearningLanguage) == null
            ? 0
            : IndexOf(cfg.LearningLanguage) + 1;
        ImGui.SetNextItemWidth(Ui.FieldWidth);
        if (ImGui.Combo(L("Also show messages in"), ref l, learningNames, learningNames.Length))
        {
            cfg.LearningLanguage = l == 0 ? string.Empty : Languages.All[l - 1].Code;
            changed = true;
        }

        if (!string.IsNullOrWhiteSpace(cfg.LearningLanguage))
        {
            using (ImRaii.PushIndent())
            {
                if (cfg.LearningLanguage == cfg.PreferredLanguage)
                    Ui.Status(L("This is the language you read best: messages are already translated into it."), StatusKind.Warning);

                ImGui.TextUnformatted(L("Only for these players (comma separated, empty = everyone):"));
                ImGui.SetNextItemWidth(-1);
                if (ImGui.InputTextWithHint("##learningplayers", L("e.g. Hanna Weber"), ref learningPlayersText, 1000))
                    cfg.LearningPlayers = SplitList(learningPlayersText);
                changed |= Committed();

                var untranslated = cfg.LearningForUntranslated;
                if (ImGui.Checkbox(L("Also for messages already in the language I read best"), ref untranslated))
                {
                    cfg.LearningForUntranslated = untranslated;
                    changed = true;
                }
                ImGuiComponents.HelpMarker(L("Off: only messages that get translated also get the learning line.\n" +
                                           "On: messages written in your own language get it too (more engine requests)."));

                var lcolor = cfg.LearningTagColor;
                if (ColorPicker.Draw("learningcolor", L("Learning tag colour"), ref lcolor))
                {
                    cfg.LearningTagColor = lcolor;
                    changed = true;
                }
            }
        }

        if (changed)
        {
            plugin.Translator.ClearCache();
            plugin.SaveConfig();
        }
    }

    /// <summary>Which languages of incoming chat are translated: all except the ones you understand, or only a few.</summary>
    private void DrawLanguageFilter(ref bool changed)
    {
        var cfg = plugin.Configuration;
        Ui.Section(L("Languages to translate"), FontAwesomeIcon.Filter,
                   help: L("Messages in a language you understand are left as they are: they cost no request and do not fill " +
                           "your chat. LinguaBridge recognises most messages by itself, without asking the engine."));

        var mode = (int)cfg.LanguageMode;
        if (ImGui.RadioButton(L("Every language, except the ones I understand"), ref mode, 0))
        {
            cfg.LanguageMode = IncomingLanguageMode.AllExcept;
            changed = true;
        }
        if (cfg.LanguageMode == IncomingLanguageMode.AllExcept)
            LanguageChecklist("understood", cfg.UnderstoodLanguages, alwaysOn: cfg.IncomingOnlyToPreferred ? cfg.PreferredLanguage : null,
                              alwaysOnHint: L("The language you read best is never translated."), ref changed);

        if (ImGui.RadioButton(L("Only these languages"), ref mode, 1))
        {
            cfg.LanguageMode = IncomingLanguageMode.Only;
            if (cfg.OnlyLanguages.Count == 0)
                cfg.OnlyLanguages = new[] { cfg.LanguageA, cfg.LanguageB }.Where(c => c != cfg.PreferredLanguage).Distinct().ToList();
            changed = true;
        }
        if (cfg.LanguageMode == IncomingLanguageMode.Only)
        {
            LanguageChecklist("only", cfg.OnlyLanguages, alwaysOn: null, alwaysOnHint: null, ref changed);
            if (cfg.OnlyLanguages.Count == 0)
                Ui.Status(L("No language ticked: nothing will be translated."), StatusKind.Warning);
        }

        ImGui.Dummy(Ui.S(0, 4));
        ImGui.TextUnformatted(L("Never translate these players (comma separated):"));
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputTextWithHint("##neverplayers", L("e.g. Hanna Weber, Rukenu"), ref neverPlayersText, 1000))
            cfg.NeverTranslatePlayers = SplitList(neverPlayersText);
        changed |= Committed();
        ImGui.TextUnformatted(L("Always translate these players, whatever their language (comma separated):"));
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputTextWithHint("##alwaysplayers", L("e.g. Hanna Weber"), ref alwaysPlayersText, 1000))
            cfg.AlwaysTranslatePlayers = SplitList(alwaysPlayersText);
        changed |= Committed();
        Ui.Hint(L("A first name alone (Hanna) matches that player; part of a name (Han) does not."));
    }

    /// <summary>Three columns of language checkboxes bound to <paramref name="list"/>.</summary>
    private static void LanguageChecklist(string id, List<string> list, string? alwaysOn, string? alwaysOnHint, ref bool changed)
    {
        using var indent = ImRaii.PushIndent();
        using var table = ImRaii.Table("##" + id, 3, ImGuiTableFlags.SizingStretchSame);
        if (!table)
            return;
        foreach (var lang in Languages.All)
        {
            ImGui.TableNextColumn();
            if (lang.Code == alwaysOn)
            {
                var on = true;
                using (ImRaii.Disabled())
                    ImGui.Checkbox($"{L(lang.Name)}##{id}{lang.Code}", ref on);
                if (alwaysOnHint != null)
                    Ui.Tooltip(alwaysOnHint);
                continue;
            }
            var ticked = list.Contains(lang.Code);
            if (ImGui.Checkbox($"{L(lang.Name)}##{id}{lang.Code}", ref ticked))
            {
                if (ticked) list.Add(lang.Code);
                else list.RemoveAll(c => c == lang.Code);
                changed = true;
            }
        }
    }
}
