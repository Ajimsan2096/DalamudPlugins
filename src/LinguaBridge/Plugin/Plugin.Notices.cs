using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.Chat;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Command;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using LinguaBridge.Chat;
using LinguaBridge.Translation;
using LinguaBridge.Tutorial;
using LinguaBridge.Localization;
using LinguaBridge.Windows;
using static LinguaBridge.Localization.Loc;

namespace LinguaBridge;

/// <summary>Messages to the user about the engines: fallbacks, paused providers, Google pauses, errors (without flooding the chat).</summary>
public sealed partial class Plugin
{
    private DateTime lastErrorPrinted = DateTime.MinValue;
    private DateTime lastFallbackPrinted = DateTime.MinValue;


    private void OnFallbackUsed(string reason)
    {
        Tutorial.ShowTip(TourContent.FirstFallback);
        if (DateTime.Now - lastFallbackPrinted < TimeSpan.FromMinutes(5))
            return;
        lastFallbackPrinted = DateTime.Now;
        _ = Framework.RunOnFrameworkThread(() =>
            ChatGui.PrintError(LF("{0}. Using the backup engine for now.", reason), Tag));
    }

    private readonly Dictionary<string, DateTime> llmNoticeTimes = new();

    private void OnLlmProviderUnavailable(string provider, string reason)
    {
        lock (llmNoticeTimes)
        {
            if (llmNoticeTimes.TryGetValue(provider, out var t) && DateTime.Now - t < TimeSpan.FromMinutes(5))
                return;
            llmNoticeTimes[provider] = DateTime.Now;
        }
        _ = Framework.RunOnFrameworkThread(() => ChatGui.Print(LF("AI {0}: {1}, trying the next one.", provider, reason), Tag));
    }

    private void OnGooglePaused(TimeSpan duration, string reason)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling(duration.TotalMinutes));
        _ = Framework.RunOnFrameworkThread(() =>
        {
            UpdateDtr();
            if (Translator.ActiveEngineName != "Google")
            {
                Log.Information($"Google (backup) paused for {minutes} min: {reason}");
                return;
            }
            var backup = Translator.DeepL.Keys.Count > 0 ? "DeepL" : Translator.Llm.ActiveProviders.Count > 0 ? L("AI") : null;
            ChatGui.Print(backup != null
                ? LF("Google is limiting requests ({0}): it pauses for {1} min and resumes by itself. {2} translates meanwhile.",
                     reason, minutes, backup)
                : LF("Google is limiting requests ({0}): translation pauses for {1} min and resumes by itself. " +
                     "Messages are shown untranslated meanwhile.", reason, minutes), Tag);
        });
    }

    private void OnGoogleResumed() => _ = Framework.RunOnFrameworkThread(() =>
    {
        UpdateDtr();
        if (Translator.ActiveEngineName == "Google")
            ChatGui.Print(L("Google answers again: translation resumed."), Tag);
    });

    private void OnGlossarySaved() => _ = Framework.RunOnFrameworkThread(() => PluginInterface.SavePluginConfig(Configuration));

    private void OnDeepLKeyUnavailable(int index, string reason)
    {
        var msg = index + 2 <= Translator.DeepL.Keys.Count
            ? LF("DeepL key #{0}: {1}, trying key #{2}.", index + 1, reason, index + 2)
            : LF("DeepL key #{0}: {1}, trying the remaining keys.", index + 1, reason);
        _ = Framework.RunOnFrameworkThread(() => ChatGui.Print(msg, Tag));
    }

    private void ReportError(Exception ex, bool force)
    {
        // A planned pause (Google rate limit): announced once when it starts, not for every message.
        if (ex is EnginePausedException && !force)
        {
            Log.Debug(ex.Message);
            return;
        }
        var msg = ex is TranslationException or ArgumentException ? ex.Message : LF("Unexpected error: {0}", ex.Message);
        if (ex is not TranslationException and not ArgumentException)
            Log.Error(ex, "LinguaBridge error");
        else
            Log.Warning(msg);

        // Don't spam the chat when the engine is down: at most one error per 30 s for incoming messages.
        if (!force && DateTime.Now - lastErrorPrinted < TimeSpan.FromSeconds(30))
            return;
        lastErrorPrinted = DateTime.Now;
        _ = Framework.RunOnFrameworkThread(() => ChatGui.PrintError(msg, Tag));
    }
}
