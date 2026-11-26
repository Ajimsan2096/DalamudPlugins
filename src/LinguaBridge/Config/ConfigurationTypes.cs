using System;
using System.Linq;

namespace LinguaBridge;

// Types stored in the plugin configuration. Keep their names and namespace: saved configs refer to them.

public enum IncomingLanguageMode
{
    /// <summary>Every language except the ones in <see cref="Configuration.UnderstoodLanguages"/>.</summary>
    AllExcept = 0,
    /// <summary>Only the languages in <see cref="Configuration.OnlyLanguages"/>.</summary>
    Only = 1,
}

public enum IncomingDisplay
{
    /// <summary>Original message, then a translated line below it.</summary>
    Below = 0,
    /// <summary>Only the translation is shown (in the original's place and order). Original kept in /lb history.</summary>
    Replace = 1,
    /// <summary>Replace only while roleplay mode is on (long RP posts), below otherwise.</summary>
    ReplaceInRoleplay = 2,
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
            "male" => "male (he/him; French: il, masculine agreement; Russian: masculine past tense and adjectives, сделал / рад)",
            "female" => "female (she/her; French: elle, feminine agreement; Russian: feminine past tense and adjectives, сделала / рада)",
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
