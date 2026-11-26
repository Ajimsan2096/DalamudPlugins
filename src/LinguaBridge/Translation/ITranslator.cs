using System;
using System.Threading;
using System.Threading.Tasks;

namespace LinguaBridge.Translation;

/// <param name="Engine">Name of what actually answered (an AI provider's name), or null for the engine itself.</param>
public sealed record EngineResult(string Text, string DetectedSource, string? Engine = null);

/// <param name="Context">Recent conversation lines, sent to DeepL as (unbilled) context. Null = none.</param>
/// <param name="Roleplay">Roleplay mode: RP-aware context, markup preservation, RP formality.</param>
/// <param name="Characters">Who is who (gender, how they address each other), for agreement and tone.</param>
public sealed record TranslateOptions(string? Context = null, bool Roleplay = false, string? Characters = null)
{
    public static readonly TranslateOptions None = new();
}

public interface ITranslator
{
    string Name { get; }

    /// <param name="source">Source language, or null to let the engine detect it.</param>
    Task<EngineResult> TranslateAsync(string text, Language? source, Language target, TranslateOptions options, CancellationToken ct);
}

public enum TranslationEngine
{
    DeepL = 0,
    Google = 1,
}

/// <summary>A translation failed; the message is shown to the user (already in the interface language).</summary>
public class TranslationException(string message) : Exception(message);

/// <summary>
/// The engine is paused on purpose (rate limited, or too many requests in a short time) and resumes by itself.
/// Nothing was sent. Incoming chat is just shown untranslated meanwhile, without an error each time.
/// </summary>
public sealed class EnginePausedException(string message, TimeSpan remaining) : TranslationException(message)
{
    public TimeSpan Remaining { get; } = remaining;
}
