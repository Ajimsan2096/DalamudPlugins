using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using LinguaBridge.Translation;

namespace LinguaBridge.Chat;

/// <summary>Turns a chat SeString into translatable text, protecting links and auto-translate phrases.</summary>
public static partial class MessageReader
{
    [GeneratedRegex(@"[-]+", RegexOptions.CultureInvariant)]
    private static partial Regex PrivateUseRegex();

    [GeneratedRegex(@"^\s*https?://\S+(\s+https?://\S+)*\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex PureLinkRegex();

    [GeneratedRegex(@"^\s*<se\.\d+>\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex SoundMacroRegex();

    [GeneratedRegex(@"\p{L}{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex HasWordsRegex();

    /// <summary>
    /// Item/quest/map/status links and auto-translate phrases are already shown in your client's language,
    /// so they are kept as-is (protected) instead of being sent through the translator.
    /// </summary>
    public static string Read(SeString message)
    {
        var sb = new StringBuilder();
        var link = new StringBuilder();
        var inLink = false;

        void FlushLink()
        {
            var text = Clean(link.ToString()).Trim();
            if (text.Length > 0)
                sb.Append(' ').Append(ProtectedText.Wrap(text)).Append(' ');
            link.Clear();
        }

        foreach (var payload in message.Payloads)
        {
            switch (payload)
            {
                case ItemPayload or QuestPayload or MapLinkPayload or StatusPayload or PartyFinderPayload or PlayerPayload:
                    if (inLink)
                        FlushLink();
                    inLink = true;
                    break;
                case RawPayload when inLink: // link terminator
                    FlushLink();
                    inLink = false;
                    break;
                case AutoTranslatePayload at:
                    var phrase = Clean(at.Text).Trim();
                    if (phrase.Length > 0)
                        sb.Append(' ').Append(ProtectedText.Wrap(phrase)).Append(' ');
                    break;
                case TextPayload { Text: { } t }:
                    (inLink ? link : sb).Append(Clean(t));
                    break;
            }
        }
        if (inLink)
            FlushLink();

        return Regex.Replace(sb.ToString(), @" {2,}", " ").Trim();
    }

    private static string Clean(string s) =>
        PrivateUseRegex().Replace(s, "").Replace(ProtectedText.Open.ToString(), "").Replace(ProtectedText.Close.ToString(), "");

    /// <summary>True for messages not worth translating: links only, sound macros, no words, "gg", "o/"...</summary>
    public static bool IsNotWorthTranslating(string textWithMarkers)
    {
        var plain = ProtectedText.Plain(textWithMarkers).Trim();
        if (plain.Length < 2 || PureLinkRegex().IsMatch(plain) || SoundMacroRegex().IsMatch(plain))
            return true;
        var free = ProtectedText.WithoutProtected(textWithMarkers);
        if (!HasWordsRegex().IsMatch(free) && !free.Any(c => c is >= '぀' and <= '鿿'))
            return true; // only links / emotes / numbers
        return LanguageDetector.IsTrivial(free);
    }
}
