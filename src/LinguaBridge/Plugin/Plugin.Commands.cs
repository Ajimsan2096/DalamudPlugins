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

/// <summary>The /linguabridge (/lb) and /tr chat commands.</summary>
public sealed partial class Plugin
{

    private void OnMainCommand(string command, string args)
    {
        var trimmed = args.Trim();
        if (trimmed.Equals("clip", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("clip ", StringComparison.OrdinalIgnoreCase))
        {
            _ = TranslateClipboardCommandAsync(trimmed[4..].Trim());
            return;
        }

        switch (trimmed.ToLowerInvariant())
        {
            case "config" or "settings":
                ToggleConfigUi();
                break;
            case "wizard" or "setup":
                OpenWizard();
                break;
            case "tour" or "tutorial" or "guide":
                Tutorial.StartTour(TourContent.MainTourId);
                break;
            case "tour settings" or "tour config" or "tutorial settings" or "guide settings":
                Tutorial.StartTour(TourContent.SettingsTourId);
                break;
            case "tips on":
                Tutorial.SetTipsEnabled(true);
                ChatGui.Print(L("Tips ON."), Tag);
                break;
            case "tips off":
                Tutorial.SetTipsEnabled(false);
                ChatGui.Print(L("Tips OFF."), Tag);
                break;
            case "tips reset":
                Tutorial.ResetTips();
                ChatGui.Print(L("All tips and tour offers will be shown again."), Tag);
                break;
            case "on":
                SetIncomingEnabled(true, announce: true);
                break;
            case "off":
                SetIncomingEnabled(false, announce: true);
                break;
            case "toggle":
                SetIncomingEnabled(!Configuration.IncomingEnabled, announce: true);
                break;
            case "rp" or "roleplay":
                SetRoleplayMode(!Configuration.RoleplayMode, announce: true);
                break;
            default:
                ToggleMainUi();
                break;
        }
    }

    private void OnTranslateCommand(string command, string args)
    {
        if (string.IsNullOrWhiteSpace(args))
        {
            ToggleMainUi();
            return;
        }

        var parsed = OutgoingCommand.Parse(args, out var error);
        if (parsed == null)
        {
            ChatGui.PrintError(error ?? L("Could not parse the command."), Tag);
            return;
        }

        _ = TranslateAndSendAsync(parsed.Text, parsed.Target, parsed.ChannelPrefix);
    }
}
