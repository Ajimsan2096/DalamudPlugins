using System.Threading;
using System.Threading.Tasks;

namespace LinguaBridge.Translation;

public sealed record EngineResult(string Text, string DetectedSource);

/// <param name="Context">Recent conversation lines, sent to DeepL as (unbilled) context. Null = none.</param>
/// <param name="Roleplay">Roleplay mode: RP-aware context, markup preservation, RP formality.</param>
public sealed record TranslateOptions(string? Context = null, bool Roleplay = false)
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
