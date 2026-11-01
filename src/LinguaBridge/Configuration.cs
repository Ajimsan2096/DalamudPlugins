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

    // --- Engine ---
    public TranslationEngine Engine { get; set; } = TranslationEngine.Google;
    public string DeepLApiKey { get; set; } = string.Empty;
    /// <summary>"default", "prefer_less" (tu) or "prefer_more" (vous).</summary>
    public string DeepLFormality { get; set; } = "prefer_less";
    /// <summary>DeepL target variant for English: "EN-US" or "EN-GB".</summary>
    public string EnglishVariant { get; set; } = "EN-US";

    // --- Languages ---
    public string LanguageA { get; set; } = "EN";
    public string LanguageB { get; set; } = "FR";
    /// <summary>Also translate messages in languages outside the pair (into language B).</summary>
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
}
