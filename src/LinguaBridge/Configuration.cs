using System;
using System.Collections.Generic;
using System.Linq;
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
    /// <summary>Legacy single key (v1.0-1.1); migrated into <see cref="DeepLApiKeys"/> on load.</summary>
    public string DeepLApiKey { get; set; } = string.Empty;
    /// <summary>DeepL keys in order of use; the next one is tried when one runs out of quota.</summary>
    public List<string> DeepLApiKeys { get; set; } = new();
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
    /// <summary>
    /// Incoming chat is only translated INTO the preferred language (messages already in it are left alone).
    /// When false, the pair works both ways (A to B and B to A). /tr is always two-way.
    /// </summary>
    public bool IncomingOnlyToPreferred { get; set; } = true;
    /// <summary>Words never translated (character names, FC name, nicknames...).</summary>
    public List<string> ProtectedTerms { get; set; } = new();
    /// <summary>Also translate messages in languages outside the pair (into the preferred language).</summary>
    public bool TranslateOtherLanguages { get; set; } = true;

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

    // --- Roleplay & DeepL customisation ---
    /// <summary>
    /// Roleplay mode: RP-aware DeepL context, *action* / "speech" / ((OOC)) preserved, emotes translated,
    /// macro-spam filter off (RP posts often arrive as several quick lines), long posts split when sending.
    /// </summary>
    public bool RoleplayMode { get; set; } = false;
    /// <summary>Formality used in roleplay mode ("default" lets DeepL follow the scene).</summary>
    public string RpFormality { get; set; } = "default";
    /// <summary>Extra description of your setting/characters sent to DeepL as context in RP mode (not billed).</summary>
    public string RpSceneNotes { get; set; } = string.Empty;
    /// <summary>Last N lines of the same conversation are sent to DeepL as context (not billed). 0 = off.</summary>
    public int ContextLines { get; set; } = 4;
    /// <summary>Never translate the sender's name, your own name, and RP shorthand (IC, OOC...).</summary>
    public bool AutoProtectNames { get; set; } = true;
    /// <summary>Split translated messages longer than the 500-byte chat limit into several lines.</summary>
    public bool SplitLongMessages { get; set; } = true;
    /// <summary>Add (1/2), (2/2) markers when splitting.</summary>
    public bool SplitMarkers { get; set; } = true;
    /// <summary>Who is who: gender and notes per character, sent to DeepL when that character is in the conversation.</summary>
    public List<CharacterProfile> Characters { get; set; } = new();
    /// <summary>Your own term translations, uploaded to DeepL as glossaries (one per language direction).</summary>
    public List<GlossaryEntry> Glossary { get; set; } = new();
    /// <summary>Internal: DeepL glossary ids per key fingerprint and direction ("hash:id").</summary>
    public Dictionary<string, string> DeepLGlossaryIds { get; set; } = new();

    // --- AI (OpenAI-compatible LLMs) ---
    /// <summary>When to use the AI providers instead of DeepL/Google.</summary>
    public LlmUsage LlmUsage { get; set; } = LlmUsage.RoleplayOnly;
    /// <summary>Providers tried in order; DeepL/Google are used when all of them fail.</summary>
    public List<LlmProvider> LlmProviders { get; set; } = new();
    /// <summary>Custom system prompt ({target} = target language). Empty = built-in prompt.</summary>
    public string LlmPrompt { get; set; } = string.Empty;
    public float LlmTemperature { get; set; } = 0.3f;

    /// <summary>Moves settings from older versions into their current shape. Returns true if anything changed.</summary>
    public bool Migrate()
    {
        var changed = false;
        if (!string.IsNullOrWhiteSpace(DeepLApiKey))
        {
            if (!DeepLApiKeys.Contains(DeepLApiKey.Trim()))
                DeepLApiKeys.Insert(0, DeepLApiKey.Trim());
            DeepLApiKey = string.Empty;
            changed = true;
        }
        if (Version < 2)
        {
            Version = 2;
            changed = true;
        }
        return changed;
    }
}

public enum LlmUsage
{
    Off = 0,
    RoleplayOnly = 1,
    Always = 2,
}

[Serializable]
public sealed class LlmProvider
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Custom";
    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;

    /// <summary>Ready-made settings. Model names change often: use "List models" to pick a current one.</summary>
    public static readonly (string Name, string Url, string Model, string KeyPage, string Note)[] Presets =
    {
        ("Mistral", "https://api.mistral.ai/v1", "mistral-small-latest", "https://console.mistral.ai/api-keys",
            "Free plan (phone verification), ~1 request/s. Very good FR/ES, permissive."),
        ("Groq", "https://api.groq.com/openai/v1", "llama-3.3-70b-versatile", "https://console.groq.com/keys",
            "Free, very fast. Daily request/token caps per model."),
        ("Cerebras", "https://api.cerebras.ai/v1", "llama-3.3-70b", "https://cloud.cerebras.ai",
            "Free, large daily token quota. Good backup."),
        ("OpenRouter", "https://openrouter.ai/api/v1", "meta-llama/llama-3.3-70b-instruct:free", "https://openrouter.ai/keys",
            "Many models; ':free' ones limited to ~50 requests/day unless you bought credits once."),
        ("DeepSeek", "https://api.deepseek.com/v1", "deepseek-chat", "https://platform.deepseek.com/api_keys",
            "Paid but very cheap. Servers in China."),
        ("Gemini", "https://generativelanguage.googleapis.com/v1beta/openai", "gemini-2.5-flash", "https://aistudio.google.com/apikey",
            "Excellent quality; may block explicit content."),
        ("Local (Ollama / LM Studio)", "http://localhost:11434/v1", "", "https://ollama.com",
            "Runs on your PC: free and fully private, needs a decent GPU."),
    };
}

[Serializable]
public sealed class CharacterProfile
{
    public static readonly string[] Genders = { "unspecified", "male", "female", "non-binary" };

    public string Name { get; set; } = string.Empty;
    public string Gender { get; set; } = "unspecified";
    /// <summary>Free text: relationship, how they speak, nicknames ("calls Ysilia 'princess', speaks casually").</summary>
    public string Notes { get; set; } = string.Empty;

    public string Describe()
    {
        var gender = Gender switch
        {
            "male" => "male (he/him; French: il, masculine agreement)",
            "female" => "female (she/her; French: elle, feminine agreement)",
            "non-binary" => "non-binary (they/them)",
            _ => null,
        };
        var parts = new[] { gender, string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim() }.Where(p => p != null);
        return $"- {Name.Trim()}: {string.Join("; ", parts)}";
    }
}

[Serializable]
public sealed class GlossaryEntry
{
    public string From { get; set; } = "EN";
    public string To { get; set; } = "FR";
    public string Source { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
}
