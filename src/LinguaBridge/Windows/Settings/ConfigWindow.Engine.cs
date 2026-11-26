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

/// <summary>Settings > Engine: DeepL keys and options, Google fallback and pause state.</summary>
public sealed partial class ConfigWindow
{
    private void DrawEngine()
    {
        var cfg = plugin.Configuration;
        var changed = false;

        Ui.Section(L("Translation engine"), FontAwesomeIcon.Language, first: true);
        var engine = (int)cfg.Engine;
        string[] engines = { L("DeepL (best quality, free API key)"), L("Google Translate (no key needed)") };
        ImGui.SetNextItemWidth(-1);
        if (ImGui.Combo("##engine", ref engine, engines, engines.Length))
        {
            cfg.Engine = (TranslationEngine)engine;
            plugin.Translator.ClearCache();
            changed = true;
        }

        if (cfg.Engine == TranslationEngine.DeepL)
        {
            Ui.Section(L("DeepL API keys"), FontAwesomeIcon.Key,
                   help: L("Keys are used in order. When one runs out of its monthly quota (or is invalid), the " +
                                       "next one is used automatically for the same message. Google is only used when " +
                                       "all keys fail (if the fallback below is on)."));

            var deepl = plugin.Translator.DeepL;
            var keys = cfg.DeepLApiKeys;
            var remove = -1;
            var moveUp = -1;
            using (var table = ImRaii.Table("keys", 3, ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp))
            {
                if (table)
                {
                    ImGui.TableSetupColumn("key", ImGuiTableColumnFlags.WidthFixed, Ui.S(190));
                    ImGui.TableSetupColumn("status", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableSetupColumn("actions", ImGuiTableColumnFlags.WidthFixed, Ui.S(64));
                    for (var i = 0; i < keys.Count; i++)
                    {
                        using var id = ImRaii.PushId(i);
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGui.AlignTextToFramePadding();
                        ImGui.TextUnformatted($"#{i + 1}  {(showKey ? keys[i] : DeepLTranslator.Mask(keys[i]))}");
                        ImGui.TableNextColumn();
                        ImGui.AlignTextToFramePadding();
                        Ui.MutedText(deepl.Describe(keys[i]));
                        ImGui.TableNextColumn();
                        using (ImRaii.Disabled(i == 0))
                        {
                            if (Ui.GhostIconButton("up", FontAwesomeIcon.ArrowUp, L("Use this key earlier")))
                                moveUp = i;
                        }
                        ImGui.SameLine();
                        if (Ui.GhostIconButton("rm", FontAwesomeIcon.TrashAlt, L("Remove this key"), Ui.Danger))
                            remove = i;
                    }
                }
            }
            if (keys.Count == 0)
                Ui.Status(L("No key yet: translations use Google until you add one."), StatusKind.Warning);
            if (remove >= 0)
            {
                keys.RemoveAt(remove);
                deepl.ResetKeyStates();
                changed = true;
            }
            if (moveUp > 0)
            {
                (keys[moveUp - 1], keys[moveUp]) = (keys[moveUp], keys[moveUp - 1]);
                deepl.ResetKeyStates();
                changed = true;
            }

            ImGui.SetNextItemWidth(-(Ui.IconTextButtonWidth(FontAwesomeIcon.Plus, L("Add key")) + ImGui.GetFrameHeight() +
                                     ImGui.CalcTextSize(L("Show")).X + ImGui.GetStyle().ItemSpacing.X * 3 + ImGui.GetStyle().ItemInnerSpacing.X));
            ImGui.InputTextWithHint("##newkey", L("Paste a DeepL API key (ends in :fx for Free)"), ref newKey, 128,
                                    showKey ? ImGuiInputTextFlags.None : ImGuiInputTextFlags.Password);
            ImGui.SameLine();
            using (ImRaii.Disabled(string.IsNullOrWhiteSpace(newKey)))
            {
                if (Ui.Button(FontAwesomeIcon.Plus, L("Add key"), primary: true))
                {
                    var k = newKey.Trim();
                    if (!keys.Contains(k))
                        keys.Add(k);
                    newKey = string.Empty;
                    deepl.ResetKeyStates();
                    changed = true;
                    _ = deepl.GetUsageAsync(k, default).ContinueWith(_ => { }); // show status/usage right away
                }
            }
            ImGui.SameLine();
            ImGui.Checkbox(L("Show"), ref showKey);

            using (ImRaii.Disabled(checkingUsage || keys.Count == 0))
            {
                if (Ui.Button(FontAwesomeIcon.SyncAlt, L("Check usage")))
                {
                    checkingUsage = true;
                    _ = deepl.RefreshAllUsageAsync(default).ContinueWith(_ => checkingUsage = false);
                }
            }
            ImGui.SameLine();
            if (Ui.Button(FontAwesomeIcon.Redo, L("Retry used-up keys"),
                          tooltip: L("Keys that ran out are skipped for a few hours. Use this after your monthly quota resets.")))
                deepl.ResetKeyStates();

            Ui.Hint(L("Free plan: 500,000 characters/month per account (deepl.com > API > \"DeepL API Free\"). " +
                              "Keys are stored in plain text in your Dalamud plugin config folder."));

            Ui.Section(L("DeepL options"), FontAwesomeIcon.Cog);
            var f = Array.FindIndex(Formalities, x => x.Value == cfg.DeepLFormality);
            if (f < 0) f = 0;
            ImGui.SetNextItemWidth(Ui.S(200));
            if (ImGui.Combo(L("Formality (FR/DE/ES/IT/PT/NL/JA/RU)"), ref f, Formalities.Select(x => L(x.Label)).ToArray(), Formalities.Length))
            {
                cfg.DeepLFormality = Formalities[f].Value;
                plugin.Translator.ClearCache();
                changed = true;
            }

            var gb = cfg.EnglishVariant == "EN-GB";
            if (ImGui.Checkbox(L("British English output (instead of American)"), ref gb))
            {
                cfg.EnglishVariant = gb ? "EN-GB" : "EN-US";
                plugin.Translator.ClearCache();
                changed = true;
            }
        }
        if (cfg.Engine == TranslationEngine.DeepL)
        {
            var fb = cfg.FallbackToGoogle;
            if (ImGui.Checkbox(L("Use Google automatically if DeepL fails"), ref fb))
            {
                cfg.FallbackToGoogle = fb;
                changed = true;
            }
            ImGuiComponents.HelpMarker(L("Quota used up, network error or wrong key: the message is translated with Google " +
                                       "instead of being lost. You get one warning in chat every 5 minutes at most."));
        }
        else
        {
            Ui.Hint(L("Google's free web endpoint works without setup, but it is unofficial, can rate-limit " +
                              "heavy use, and is a bit less natural for English/French than DeepL."));
        }

        if (plugin.Translator.GoogleInUse)
            DrawGoogleStatus();

        ImGui.Dummy(Ui.S(0, 8));
        Ui.MutedText(LF("Sent this session: {0:N0} requests, {1:N0} characters",
                        plugin.Translator.RequestsSent, plugin.Translator.CharactersSent));

        if (changed)
            plugin.SaveConfig();
    }

    /// <summary>Google's pause state, with a way to try again right away.</summary>
    private void DrawGoogleStatus()
    {
        Ui.Section(L("Google"), FontAwesomeIcon.Globe,
                   help: L("To avoid being blocked, LinguaBridge sends Google at most a few requests per minute. If Google " +
                           "still answers \"too many requests\", translation pauses (2, 5, 10, 20, then 30 minutes), then " +
                           "LinguaBridge checks by itself whether Google answers again and carries on. Meanwhile messages " +
                           "are shown untranslated, or translated by DeepL / AI if you set one up."));
        if (plugin.Translator.Google.PauseRemaining is { } left)
        {
            Ui.Status(LF("Paused (too many requests): resumes by itself at {0:HH:mm}.", DateTime.Now + left), StatusKind.Warning);
            if (Ui.Button(FontAwesomeIcon.Redo, L("Retry now"), tooltip: L("Checks right away whether Google answers again.")))
                plugin.Translator.Google.ResumeNow();
        }
        else
        {
            Ui.Status(L("Available."), StatusKind.Success);
        }
    }
}
