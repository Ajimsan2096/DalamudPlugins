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

/// <summary>Small chat helpers: names, channel names, reply prefixes, emote subjects.</summary>
public sealed partial class Plugin
{
    private static bool IsEmotePrefix(string prefix) =>
        prefix.Trim().Equals("/em", StringComparison.OrdinalIgnoreCase) ||
        prefix.Trim().Equals("/emote", StringComparison.OrdinalIgnoreCase);

    /// <summary>Japanese topic / subject particles left after the name ("Nameは微笑む" -> "微笑む").</summary>
    private static readonly string[] JapaneseParticles = { "は、", "が、", "は", "が", "も", "さんは", "さんが" };

    private static string StripLeadingName(string text, string name)
    {
        var t = text.TrimStart();
        string rest;
        if (t.StartsWith(name, StringComparison.OrdinalIgnoreCase))
            rest = t[name.Length..];
        else if (name.Split(' ')[0] is { Length: >= 3 } first &&
                 (t.StartsWith(first + " ", StringComparison.OrdinalIgnoreCase) ||
                  (t.StartsWith(first, StringComparison.OrdinalIgnoreCase) && t.Length > first.Length && !char.IsLetter(t[first.Length]))))
            rest = t[first.Length..];
        else
            return t;

        // Japanese: no space after the name, a particle instead.
        foreach (var particle in JapaneseParticles)
            if (rest.StartsWith(particle, StringComparison.Ordinal))
                return rest[particle.Length..].TrimStart();
        // German / Dutch / English possessive or case forms glued to the name ("Name's", "Names") are not stripped.
        if (rest.Length > 0 && char.IsLetter(rest[0]))
            return t;
        return rest.TrimStart(' ', ',', '、');
    }

    private static string StripGlyphs(string s) =>
        new string(s.Where(c => c is < '' or > '').ToArray()).Trim();

    /// <summary>The chat command that answers a message in the same place it came from.</summary>
    public static string? ReplyPrefixFor(XivChatType type, string sender, string? world) => type switch
    {
        XivChatType.Say => "/s ",
        XivChatType.Yell => "/y ",
        XivChatType.Shout => "/sh ",
        XivChatType.Party or XivChatType.CrossParty => "/p ",
        XivChatType.Alliance => "/a ",
        XivChatType.FreeCompany => "/fc ",
        XivChatType.CustomEmote => "/em ",
        XivChatType.NoviceNetwork => "/n ",
        XivChatType.PvPTeam => "/pvpteam ",
        XivChatType.TellIncoming => string.IsNullOrEmpty(world) ? "/r " : $"/t {sender}@{world} ",
        >= XivChatType.Ls1 and <= XivChatType.Ls8 => $"/l{type - XivChatType.Ls1 + 1} ",
        XivChatType.CrossLinkShell1 => "/cwl1 ",
        >= XivChatType.CrossLinkShell2 and <= XivChatType.CrossLinkShell8 => $"/cwl{type - XivChatType.CrossLinkShell2 + 2} ",
        _ => null,
    };

    public static string ChannelName(XivChatType type) => type switch
    {
        XivChatType.TellIncoming => "Tell",
        XivChatType.TellOutgoing => "Tell (out)",
        XivChatType.CrossParty => "Party",
        XivChatType.FreeCompany => "FC",
        XivChatType.CustomEmote => "Emote",
        >= XivChatType.Ls1 and <= XivChatType.Ls8 => $"LS{type - XivChatType.Ls1 + 1}",
        XivChatType.CrossLinkShell1 => "CWLS1",
        >= XivChatType.CrossLinkShell2 and <= XivChatType.CrossLinkShell8 => $"CWLS{type - XivChatType.CrossLinkShell2 + 2}",
        _ => type.ToString(),
    };
}
