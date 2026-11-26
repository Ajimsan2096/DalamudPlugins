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

/// <summary>Settings > AI: OpenAI-compatible providers, prompt and temperature.</summary>
public sealed partial class ConfigWindow
{
    private void DrawAi()
    {
        var cfg = plugin.Configuration;
        var llm = plugin.Translator.Llm;
        var changed = false;

        Ui.Section(L("AI translation"), FontAwesomeIcon.Robot, first: true);
        Ui.Hint(L("Use AI models (any OpenAI-compatible API) as translator. They understand idioms, tone and " +
                          "whole scenes much better than DeepL; best for roleplay. Providers are tried in order; " +
                          "if all fail, DeepL / Google are used."));

        string[] usages = { L("Off"), L("Only in roleplay mode (recommended)"), L("Always") };
        var usage = (int)cfg.LlmUsage;
        ImGui.SetNextItemWidth(Ui.S(260));
        if (ImGui.Combo(L("When to use AI"), ref usage, usages, usages.Length))
        {
            cfg.LlmUsage = (LlmUsage)usage;
            plugin.Translator.ClearCache();
            changed = true;
        }
        Ui.Status(plugin.Translator.UsingLlm ? L("AI is active right now.") :
                  llm.ActiveProviders.Count == 0 ? L("No provider configured yet.") : L("AI is not active in the current mode."),
                  plugin.Translator.UsingLlm ? StatusKind.Success : llm.ActiveProviders.Count == 0 ? StatusKind.Warning : StatusKind.Info);

        Ui.Section(L("Providers (tried top to bottom)"), FontAwesomeIcon.Robot);

        var remove = -1;
        var moveUp = -1;
        for (var i = 0; i < cfg.LlmProviders.Count; i++)
        {
            var p = cfg.LlmProviders[i];
            using var id = ImRaii.PushId(p.Id);
            var enabled = p.Enabled;
            if (ImGui.Checkbox("##en", ref enabled))
            {
                p.Enabled = enabled;
                changed = true;
            }
            ImGui.SameLine();
            var open = ImGui.CollapsingHeader($"#{i + 1} {p.Name}  [{p.Model}]  - {llm.Describe(p)}###hdr");
            if (!open)
                continue;

            using (ImRaii.PushIndent())
            {
                var name = p.Name;
                ImGui.SetNextItemWidth(Ui.S(200));
                if (ImGui.InputText(L("Name"), ref name, 40)) p.Name = name;
                changed |= Committed();
                var url = p.BaseUrl;
                ImGui.SetNextItemWidth(Ui.S(-90));
                if (ImGui.InputText(L("Base URL"), ref url, 200)) p.BaseUrl = url.Trim();
                changed |= Committed();
                var key = p.ApiKey;
                ImGui.SetNextItemWidth(Ui.S(-90));
                if (ImGui.InputText(L("API key"), ref key, 200, showKey ? ImGuiInputTextFlags.None : ImGuiInputTextFlags.Password))
                    p.ApiKey = key.Trim();
                changed |= Committed();
                var model = p.Model;
                ImGui.SetNextItemWidth(Ui.S(-200));
                if (ImGui.InputText(L("Model"), ref model, 120)) p.Model = model.Trim();
                changed |= Committed();
                ImGui.SameLine();
                if (ImGui.SmallButton(L("List models")))
                {
                    aiTestResults[p.Id] = (L("Loading models..."), StatusKind.Busy);
                    _ = llm.ListModelsAsync(p, default).ContinueWith(t =>
                    {
                        if (t.IsCompletedSuccessfully)
                        {
                            aiModels[p.Id] = t.Result;
                            aiTestResults[p.Id] = (LF("{0} models found", t.Result.Count), StatusKind.Info);
                        }
                        else
                        {
                            aiTestResults[p.Id] = (L("Could not list models: ") + t.Exception?.GetBaseException().Message, StatusKind.Error);
                        }
                    });
                }
                if (aiModels.TryGetValue(p.Id, out var models) && models.Count > 0)
                {
                    var current = Math.Max(0, models.IndexOf(p.Model));
                    ImGui.SetNextItemWidth(Ui.S(-200));
                    if (ImGui.Combo(L("Pick model"), ref current, models.ToArray(), models.Count))
                    {
                        p.Model = models[current];
                        changed = true;
                    }
                }

                if (Ui.Button(FontAwesomeIcon.Magic, L("Test")))
                {
                    aiTestResults[p.Id] = (L("Testing..."), StatusKind.Busy);
                    _ = llm.TestAsync(p, default).ContinueWith(t => aiTestResults[p.Id] = t.IsCompletedSuccessfully
                        ? (t.Result.Text, t.Result.Ok ? StatusKind.Success : StatusKind.Error)
                        : (L("Failed: ") + t.Exception?.GetBaseException().Message, StatusKind.Error));
                }
                ImGui.SameLine();
                using (ImRaii.Disabled(i == 0))
                    if (Ui.Button(FontAwesomeIcon.ArrowUp, L("Move up")))
                        moveUp = i;
                ImGui.SameLine();
                if (Ui.Button(FontAwesomeIcon.TrashAlt, L("Remove")))
                    remove = i;
                if (aiTestResults.TryGetValue(p.Id, out var res))
                    Ui.Status(res.Text, res.Kind);
                var preset = LlmProvider.Presets.FirstOrDefault(x => x.Url == p.BaseUrl);
                if (preset.Name != null)
                {
                    Ui.MutedText(preset.Note);
                    if (ImGui.SmallButton(L("Copy key page link")))
                        ImGui.SetClipboardText(preset.KeyPage);
                }
            }
            ImGui.Spacing();
        }
        if (remove >= 0)
        {
            cfg.LlmProviders.RemoveAt(remove);
            changed = true;
        }
        if (moveUp > 0)
        {
            (cfg.LlmProviders[moveUp - 1], cfg.LlmProviders[moveUp]) = (cfg.LlmProviders[moveUp], cfg.LlmProviders[moveUp - 1]);
            changed = true;
        }

        var presetNames = LlmProvider.Presets.Select(x => x.Name).Append(L("Custom (any OpenAI-compatible URL)")).ToArray();
        ImGui.Dummy(Ui.S(0, 4));
        ImGui.SetNextItemWidth(Ui.S(260));
        ImGui.Combo("##preset", ref presetIndex, presetNames, presetNames.Length);
        ImGui.SameLine();
        if (Ui.Button(FontAwesomeIcon.Plus, L("Add provider"), primary: true))
        {
            var p = new LlmProvider();
            if (presetIndex < LlmProvider.Presets.Length)
            {
                var x = LlmProvider.Presets[presetIndex];
                p.Name = x.Name;
                p.BaseUrl = x.Url;
                p.Model = x.Model;
                ImGui.SetClipboardText(x.KeyPage);
                aiTestResults[p.Id] = (LF("Key page copied to clipboard: {0}  - paste your key above, then Test.", x.KeyPage), StatusKind.Info);
            }
            cfg.LlmProviders.Add(p);
            changed = true;
        }
        ImGui.SameLine();
        ImGui.Checkbox(L("Show keys"), ref showKey);
        if (Ui.Button(FontAwesomeIcon.Redo, L("Retry paused providers")))
            llm.ResetStates();

        ImGui.Dummy(Ui.S(0, 8));
        if (ImGui.CollapsingHeader(L("Advanced: prompt and temperature")))
        {
            var temp = cfg.LlmTemperature;
            ImGui.SetNextItemWidth(Ui.S(150));
            if (ImGui.SliderFloat(L("Temperature"), ref temp, 0f, 1f, "%.2f"))
                cfg.LlmTemperature = temp;
            changed |= Committed();
            ImGuiComponents.HelpMarker(L("0.2-0.4 keeps translations faithful. Higher = freer, more creative wording."));
            if (!aiPromptLoaded)
            {
                aiPrompt = string.IsNullOrWhiteSpace(cfg.LlmPrompt) ? LlmTranslator.DefaultPrompt : cfg.LlmPrompt;
                aiPromptLoaded = true;
            }
            Ui.MutedText(L("{target} is replaced by the target language. Keep the [[xx]] output rule."));
            if (ImGui.InputTextMultiline("##prompt", ref aiPrompt, 4000, new Vector2(-1, ImGui.GetTextLineHeight() * 10)))
                cfg.LlmPrompt = aiPrompt.Trim() == LlmTranslator.DefaultPrompt.Trim() ? string.Empty : aiPrompt;
            changed |= Committed();
            if (ImGui.SmallButton(L("Reset prompt")))
            {
                cfg.LlmPrompt = string.Empty;
                aiPrompt = LlmTranslator.DefaultPrompt;
                changed = true;
            }
        }

        ImGui.Dummy(Ui.S(0, 10));
        Ui.Callout(FontAwesomeIcon.ShieldAlt, Ui.Warning, L("Privacy"), L("Messages (yours and your partners') are sent to the provider you pick. Free plans may store " +
                          "them or use them for training; check each provider's terms. A local model (Ollama / " +
                          "LM Studio) keeps everything on your PC. Keys are stored in plain text in your plugin config."));

        if (changed)
        {
            plugin.Translator.ClearCache();
            plugin.SaveConfig();
        }
    }
}
