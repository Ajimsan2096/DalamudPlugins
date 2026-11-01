using System;
using System.Collections.Generic;
using Dalamud.Configuration;
using Dalamud.Game.Text;
using LinguaBridge.Translation;

namespace LinguaBridge;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public bool ShowedWizard { get; set; } = false;

    // --- Engine ---
    public TranslationEngine Engine { get; set; } = TranslationEngine.Google;
    public string DeepLApiKey { get; set; } = string.Empty;
    /// <summary>"default", "prefer_less" (tu) or "prefer_more" (vous).</summary>
    public string DeepLFormality { get; set; } = "prefer_less";
    /// <summary>DeepL target variant for English: "EN-US" or "EN-GB".</summary>
    public string EnglishVariant { get; set; } = "EN-US";
    /// <summary>If DeepL fails (quota, network, bad key) use Google for that message instead of showing nothing.</summary>
    public bool FallbackToGoogle { get; set; } = true;

    // --- Languages ---
    public string LanguageA { get; set; } = "EN";
    public string LanguageB { get; set; } = "FR";
    /// <summary>Expand chat abbreviations (jsp, tkt, idk...) before sending text to the engine.</summary>
    public bool ExpandAbbreviations { get; set; } = true;
    /// <summary>The language you read best: used for Party Finder and for languages outside the pair.</summary>
    public string PreferredLanguage { get; set; } = "EN";
    /// <summary>Words never translated (character names, FC name, nicknames...).</summary>
    public List<string> ProtectedTerms { get; set; } = new();
    /// <summary>Also translate messages in languages outside the pair (into the preferred language).</summary>
    public bool TranslateOtherLanguages { get; set; } = false;

    // --- Incoming chat ---
    public bool IncomingEnabled { get; set; } = true;
    public HashSet<XivChatType> Channels { get; set; } = new()
    {
        XivChatType.Say, XivChatType.Party, XivChatType.CrossParty, XivChatType.Alliance,
        XivChatType.TellIncoming, XivChatType.FreeCompany,
        XivChatType.Ls1, XivChatType.Ls2, XivChatType.Ls3, XivChatType.Ls4,
        XivChatType.Ls5, XivChatType.Ls6, XivChatType.Ls7, XivChatType.Ls8,
        XivChatType.CrossLinkShell1, XivChatType.CrossLinkShell2, XivChatType.CrossLinkShell3, XivChatType.CrossLinkShell4,
        XivChatType.CrossLinkShell5, XivChatType.CrossLinkShell6, XivChatType.CrossLinkShell7, XivChatType.CrossLinkShell8,
    };
    public bool TranslateOwnMessages { get; set; } = false;
    /// <summary>When not empty, only messages from these players are translated (names, case-insensitive).</summary>
    public List<string> PlayerWhitelist { get; set; } = new();
    public int MaxMessageLength { get; set; } = 400;
    /// <summary>Do not translate incoming chat while in combat (fewer distractions during pulls).</summary>
    public bool PauseInCombat { get; set; } = false;
    /// <summary>Ignore a player's message if they sent another less than 0.65 s before (macro spam).</summary>
    public bool FilterMacroSpam { get; set; } = true;

    // --- Output ---
    /// <summary>Print the translation in the same channel as the original (so it shows in the same chat tab).</summary>
    public bool PrintInSameChannel { get; set; } = true;
    public XivChatType FallbackOutputChannel { get; set; } = XivChatType.Echo;
    /// <summary>UIColor row used for the "[EN&gt;FR]" tag.</summary>
    public ushort TagColor { get; set; } = 45;
    public int HistorySize { get; set; } = 200;

    // --- Outgoing ---
    /// <summary>Print your original text in Echo after sending a translated message.</summary>
    public bool EchoOriginalOnSend { get; set; } = true;

    // --- Extras ---
    /// <summary>Show an "LB" toggle in the server info bar (top right).</summary>
    public bool ShowDtrEntry { get; set; } = true;
    /// <summary>Add "Translate" to the Party Finder listing context menu.</summary>
    public bool PartyFinderMenu { get; set; } = true;
}
