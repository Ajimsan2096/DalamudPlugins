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

/// <summary>Other parts of the game and other plugins: Aetherphone (Linkpearl) and the Party Finder menu.</summary>
public sealed partial class Plugin
{
    private const string AetherphoneInternalName = "Aetherphone";
    private bool aetherphoneLoaded;
    private long aetherphoneCheckedAt; // 0 = never checked


    /// <summary>True while the Aetherphone plugin (whose Linkpearl app mirrors the game chat) is loaded. Checked at most every 5 s.</summary>
    public bool AetherphoneLoaded
    {
        get
        {
            var now = Environment.TickCount64;
            if (aetherphoneCheckedAt != 0 && now - aetherphoneCheckedAt < 5000)
                return aetherphoneLoaded;
            aetherphoneCheckedAt = now;
            try
            {
                aetherphoneLoaded = PluginInterface.InstalledPlugins.Any(
                    p => p.IsLoaded && p.InternalName.Equals(AetherphoneInternalName, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Could not list installed plugins");
                aetherphoneLoaded = false;
            }
            return aetherphoneLoaded;
        }
    }


    private void OnContextMenuOpened(IMenuOpenedArgs args)
    {
        if (!Configuration.PartyFinderMenu || args.AddonName != "LookingForGroupDetail")
            return;
        args.AddMenuItem(new MenuItem
        {
            Name = L("Translate description"),
            UseDefaultPrefix = true,
            OnClicked = TranslatePartyFinder,
        });
    }

    private unsafe void TranslatePartyFinder(IMenuItemClickedArgs args)
    {
        try
        {
            var addon = (AddonLookingForGroupDetail*)args.AddonPtr;
            if (addon == null)
                return;
            var description = SeString.Parse(addon->DescriptionString.AsSpan()).TextValue;
            description = StripGlyphs(description).Trim();
            if (description.Length == 0)
            {
                ChatGui.Print(L("This listing has no description."), Tag);
                return;
            }
            _ = TranslatePartyFinderAsync(description);
        }
        catch (Exception ex)
        {
            ReportError(ex, force: true);
        }
    }

    private async Task TranslatePartyFinderAsync(string description)
    {
        try
        {
            var preferred = Languages.Get(Configuration.PreferredLanguage);
            var result = await Translator.TranslateAsync(description, preferred).ConfigureAwait(false);
            if (result == null)
            {
                await Framework.RunOnFrameworkThread(() =>
                    ChatGui.Print(LF("The description is already in {0}.", L(preferred.Name)), Tag));
                return;
            }

            AddHistory(new HistoryEntry(DateTime.Now, "Party Finder", "Listing", result, false, null));
            Tutorial.ShowTip(TourContent.FirstPartyFinder);
            await Framework.RunOnFrameworkThread(() => ChatGui.Print(new XivChatEntry
            {
                Type = XivChatType.Echo,
                Message = new SeStringBuilder()
                          .AddUiForeground($"[PF>{result.Target.Code}] ", Configuration.TagColor)
                          .AddText(ChatSender.FoldForGame(result.Text))
                          .Build(),
            }));
        }
        catch (Exception ex)
        {
            ReportError(ex, force: true);
        }
    }
}
