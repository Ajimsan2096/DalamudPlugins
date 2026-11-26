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

/// <summary>Settings > Glossary: your own term translations (DeepL glossaries, AI instructions).</summary>
public sealed partial class ConfigWindow
{
    private void DrawGlossary()
    {
        var cfg = plugin.Configuration;
        var names = Languages.All.Select(l => l.Code).ToArray();
        var changed = false;

        Ui.Section(L("Glossary"), FontAwesomeIcon.Book, first: true);
        Ui.Hint(L("Your own translations for specific words: lore terms, nicknames, pet names, RP slang, " +
                          "how intimate or emotional words should be rendered... DeepL always uses these when it " +
                          "knows the source language (they are uploaded to your DeepL account as glossaries, one per " +
                          "direction and per key, automatically), and the AI providers get them as instructions."));
        if (cfg.Engine == TranslationEngine.Google && cfg.LlmUsage == LlmUsage.Off)
            Ui.Status(L("Google does not use the glossary: it only applies with DeepL or AI (Settings > Engine / AI)."), StatusKind.Warning);
        Ui.MutedText(LF("Status: {0}", plugin.Translator.DeepL.GlossaryStatus));
        if (Ui.Button(FontAwesomeIcon.Magic, L("Import recommended terms")))
        {
            var added = 0;
            foreach (var (from, to, source, target) in PairStyle.RecommendedGlossary)
            {
                if (cfg.Glossary.Any(e => e.From == from && e.To == to && e.Source.Equals(source, StringComparison.OrdinalIgnoreCase)))
                    continue;
                cfg.Glossary.Add(new GlossaryEntry { From = from, To = to, Source = source, Target = target });
                added++;
            }
            importResult = added == 0 ? L("Already imported.") : LF("{0} terms added.", added);
            changed = true;
        }
        ImGuiComponents.HelpMarker(L("Endearments (mi amor > mon amour / mein Schatz, babe > bébé / Schatz, " +
                                   "mon cœur > sweetheart...) and official FFXIV job/term names between English and French " +
                                   "(Dark Knight <> Chevalier noir) and English and German (Dark Knight <> Dunkelritter, " +
                                   "Free Company <> Freie Gesellschaft...). " +
                                   "Remove any entry you don't like."));
        if (importResult.Length > 0)
        {
            ImGui.SameLine();
            Ui.MutedText(importResult);
        }

        Ui.Section(L("Add a term"), FontAwesomeIcon.Plus);

        ImGui.SetNextItemWidth(Ui.S(60));
        ImGui.Combo("##gfrom", ref glossaryFrom, names, names.Length);
        ImGui.SameLine();
        ImGui.TextUnformatted(">");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(Ui.S(60));
        ImGui.Combo("##gto", ref glossaryTo, names, names.Length);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(Ui.S(150));
        ImGui.InputTextWithHint("##gsrc", L("word or phrase"), ref newSource, 100);
        ImGui.SameLine();
        ImGui.TextUnformatted("=");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(Ui.S(150));
        ImGui.InputTextWithHint("##gdst", L("translation"), ref newTarget, 100);
        ImGui.SameLine();
        using (ImRaii.Disabled(string.IsNullOrWhiteSpace(newSource) || string.IsNullOrWhiteSpace(newTarget) ||
                               glossaryFrom == glossaryTo))
        {
            if (Ui.Button(FontAwesomeIcon.Plus, L("Add"), primary: true))
            {
                cfg.Glossary.RemoveAll(e => e.From == names[glossaryFrom] && e.To == names[glossaryTo] &&
                                            e.Source.Equals(newSource.Trim(), StringComparison.Ordinal));
                cfg.Glossary.Add(new GlossaryEntry
                {
                    From = names[glossaryFrom], To = names[glossaryTo], Source = newSource.Trim(), Target = newTarget.Trim(),
                });
                newSource = newTarget = string.Empty;
                changed = true;
            }
        }
        ImGui.SameLine();
        if (Ui.IconButton("swap", FontAwesomeIcon.ExchangeAlt,
                          L("Swap the direction. Each direction is a separate glossary: add EN>FR and FR>EN entries separately.")))
            (glossaryFrom, glossaryTo) = (glossaryTo, glossaryFrom);
        if (glossaryFrom == glossaryTo)
            Ui.Status(L("Pick two different languages."), StatusKind.Warning);

        Ui.Section(L("Terms"), FontAwesomeIcon.Search);
        Ui.Icon(FontAwesomeIcon.Search, Ui.Muted);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(Ui.S(200));
        ImGui.InputTextWithHint("##gfilter", L("filter..."), ref glossaryFilter, 50);
        ImGui.SameLine();
        Ui.MutedText(LF("{0} entries", cfg.Glossary.Count));

        var remove = -1;
        using (var table = ImRaii.Table("glossary", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.BordersInnerH,
                                        new Vector2(-1, Math.Max(ImGui.GetContentRegionAvail().Y, ImGui.GetFrameHeight() * 8))))
        {
            if (table)
            {
                ImGui.TableSetupColumn(L("Dir"), ImGuiTableColumnFlags.WidthFixed, Ui.S(60));
                ImGui.TableSetupColumn(L("Source"));
                ImGui.TableSetupColumn(L("Translation"));
                ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFrameHeight());
                ImGui.TableSetupScrollFreeze(0, 1);
                ImGui.TableHeadersRow();
                for (var i = 0; i < cfg.Glossary.Count; i++)
                {
                    var e = cfg.Glossary[i];
                    if (glossaryFilter.Length > 0 &&
                        !e.Source.Contains(glossaryFilter, StringComparison.OrdinalIgnoreCase) &&
                        !e.Target.Contains(glossaryFilter, StringComparison.OrdinalIgnoreCase))
                        continue;
                    using var id = ImRaii.PushId(i);
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextColored(Ui.Accent, $"{e.From} > {e.To}");
                    ImGui.TableNextColumn();
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextUnformatted(e.Source);
                    ImGui.TableNextColumn();
                    var target = e.Target;
                    ImGui.SetNextItemWidth(-1);
                    if (ImGui.InputText("##t", ref target, 100))
                        e.Target = target;
                    if (Committed() && e.Target.Trim().Length > 0)
                    {
                        e.Target = e.Target.Trim();
                        changed = true;
                    }
                    Ui.Tooltip(L("Click to edit the translation"));
                    ImGui.TableNextColumn();
                    if (Ui.GhostIconButton("rm", FontAwesomeIcon.TrashAlt, LF("Remove \"{0}\"", e.Source), Ui.Danger))
                        remove = i;
                }
            }
        }
        if (remove >= 0)
        {
            cfg.Glossary.RemoveAt(remove);
            changed = true;
        }

        if (changed)
        {
            plugin.Translator.ClearCache();
            plugin.SaveConfig();
        }
    }
}
