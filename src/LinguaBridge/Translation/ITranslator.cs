using System.Threading;
using System.Threading.Tasks;

namespace LinguaBridge.Translation;

public sealed record EngineResult(string Text, string DetectedSource);

public interface ITranslator
{
    string Name { get; }

    /// <param name="source">Source language, or null to let the engine detect it.</param>
    Task<EngineResult> TranslateAsync(string text, Language? source, Language target, CancellationToken ct);
}

public enum TranslationEngine
{
    DeepL = 0,
    Google = 1,
}
