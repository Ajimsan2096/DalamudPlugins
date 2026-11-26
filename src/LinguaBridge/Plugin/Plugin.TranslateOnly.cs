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

/// <summary>Translate without sending: the clipboard (/lb clip) and the "Translate only" mode of the main window.</summary>
public sealed partial class Plugin
{
    /// <summary>Translations waiting to be put on the clipboard (ImGui can only do that during Draw).</summary>
    private readonly ConcurrentQueue<string> pendingClipboard = new();


    /// <summary>
    /// Translates text without sending it anywhere (for Aetherphone's ChocoChat / Velvet, websites, Discord...).
    /// With no target: into the language you read best, or, if it is already in that language, into the other
    /// language of the pair (so the same button works for reading and for writing).
    /// Returns null when there is nothing to translate.
    /// </summary>
    public async Task<TranslationResult?> TranslateTextAsync(string text, Language? target)
    {
        text = text.Trim();
        if (text.Length == 0)
            return null;
        if (target != null)
            return await Translator.TranslateAsync(text, target).ConfigureAwait(false);
        var preferred = Languages.Get(Configuration.PreferredLanguage);
        return await Translator.TranslateAsync(text, preferred).ConfigureAwait(false)
               ?? await Translator.TranslateAsync(text).ConfigureAwait(false);
    }

    /// <summary>Puts text on the clipboard on the next frame.</summary>
    public void CopyToClipboard(string text)
    {
        if (!string.IsNullOrEmpty(text))
            pendingClipboard.Enqueue(text);
    }

    private void FlushClipboard()
    {
        string? last = null;
        while (pendingClipboard.TryDequeue(out var text))
            last = text;
        if (last != null)
            Dalamud.Bindings.ImGui.ImGui.SetClipboardText(last);
    }

    /// <summary>/lb clip [:xx] : translates the clipboard, shows the result (only to you) and copies it back.</summary>
    private async Task TranslateClipboardCommandAsync(string args)
    {
        try
        {
            Language? target = null;
            if (args.StartsWith(':'))
            {
                target = Languages.TryGet(args[1..].Trim().ToUpperInvariant());
                if (target == null)
                {
                    ChatGui.PrintError(LF("Unknown language \"{0}\". Example: /lb clip :fr", args[1..].Trim()), Tag);
                    return;
                }
            }

            var text = Clipboard.GetText()?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                ChatGui.PrintError(L("The clipboard has no text. Copy a message first (Ctrl+C), then /lb clip."), Tag);
                return;
            }
            if (text.Length > 3000)
                text = text[..3000];

            var result = await TranslateTextAsync(text, target).ConfigureAwait(false);
            if (result == null)
            {
                await Framework.RunOnFrameworkThread(() => ChatGui.Print(L("Nothing to translate: the text is already in that language."), Tag))
                               .ConfigureAwait(false);
                return;
            }

            CopyToClipboard(result.Text);
            AddHistory(new HistoryEntry(DateTime.Now, "clipboard", "Clipboard", result, false, null));
            await Framework.RunOnFrameworkThread(() => ChatGui.Print(new XivChatEntry
            {
                Type = XivChatType.Echo,
                Message = new SeStringBuilder()
                          .AddUiForeground($"[{result.Source.Code}>{result.Target.Code}] ", Configuration.TagColor)
                          .AddText(ChatSender.FoldForGame(result.Text))
                          .AddUiForeground(" (" + L("copied") + ")", 4)
                          .Build(),
            })).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ReportError(ex, force: true);
        }
    }
}
