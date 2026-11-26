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

/// <summary>The "LB" entry in the server info bar: state, engine, Google pause; click to toggle or open the window.</summary>
public sealed partial class Plugin
{
    private IDtrBarEntry? dtrEntry;


    private void UpdateDtr()
    {
        if (!Configuration.ShowDtrEntry)
        {
            dtrEntry?.Remove();
            dtrEntry = null;
            return;
        }

        dtrEntry ??= DtrBar.Get(Tag);
        var on = Configuration.IncomingEnabled;
        var engine = Translator.ActiveEngineName;
        var pausedTag = engine == "Google" && Translator.Google.PauseRemaining != null ? " (" + L("paused") + ")" : string.Empty;
        dtrEntry.Text = new SeStringBuilder()
                        .AddUiForeground(on ? (ushort)45 : (ushort)4)
                        .AddText(on ? $"LB {(Configuration.RoleplayMode ? "RP " : "")}{Configuration.LanguageA}/{Configuration.LanguageB}{pausedTag}" : "LB off")
                        .AddUiForegroundOff()
                        .Build();
        var paused = Translator.Google.PauseRemaining is { } left
            ? "\n" + LF("Google paused (too many requests): resumes by itself at {0:HH:mm}.", DateTime.Now + left)
            : string.Empty;
        dtrEntry.Tooltip = new SeString(new TextPayload(
            (on ? LF("LinguaBridge: chat translation ON ({0})", engine)
                : LF("LinguaBridge: chat translation OFF ({0})", engine)) + paused + "\n" +
            L("Left click: toggle   Right click: open window")));
        dtrEntry.OnClick = e =>
        {
            if (e.ClickType == MouseClickType.Right)
                ToggleMainUi();
            else
                SetIncomingEnabled(!Configuration.IncomingEnabled, announce: false);
        };
        dtrEntry.Shown = true;
    }
}
