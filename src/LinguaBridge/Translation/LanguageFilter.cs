using System.Collections.Generic;
using System.Linq;

namespace LinguaBridge.Translation;

/// <summary>
/// Which source languages incoming chat may be translated from: every language except the ones the user already
/// understands, or only a chosen few.
/// </summary>
public sealed class LanguageFilter
{
    private readonly HashSet<string> skip;
    private readonly HashSet<string>? only;

    /// <param name="skip">Never translated (languages the user understands).</param>
    /// <param name="only">When not null, only these are translated.</param>
    public LanguageFilter(IEnumerable<string> skip, IEnumerable<string>? only)
    {
        this.skip = new HashSet<string>(skip.Select(s => s.ToUpperInvariant()));
        this.only = only == null ? null : new HashSet<string>(only.Select(s => s.ToUpperInvariant()));
        Key = (this.only == null ? "x:" + string.Join(",", this.skip.OrderBy(s => s)) : "o:" + string.Join(",", this.only.OrderBy(s => s)));
    }

    /// <summary>Stable text form, for the translation cache.</summary>
    public string Key { get; }

    /// <summary>True when a message written in <paramref name="code"/> may be translated. Unknown languages count as
    /// "other": translated in "all except" mode, not in "only these" mode.</summary>
    public bool Allows(string? code)
    {
        var c = string.IsNullOrWhiteSpace(code) || code == "??" ? null : code.ToUpperInvariant();
        if (only != null)
            return c != null && only.Contains(c) && !skip.Contains(c);
        return c == null || !skip.Contains(c);
    }
}
