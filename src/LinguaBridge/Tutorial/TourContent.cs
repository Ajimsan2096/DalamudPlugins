using System;
using System.Collections.Generic;

namespace LinguaBridge.Tutorial;

/// <summary>Which plugin window a tour step points at (it is opened automatically).</summary>
public enum TourWindow
{
    /// <summary>No window: the bubble is shown in the middle of the screen.</summary>
    None,
    Main,
    Config,
}

/// <summary>One bubble of a guided tour.</summary>
/// <param name="Title">Bubble title.</param>
/// <param name="Body">Explanation (wrapped).</param>
/// <param name="Window">Window that must be open; the tour opens it.</param>
/// <param name="Anchor">UI element to highlight (see <see cref="TutorialService.Anchor"/>), or null.</param>
/// <param name="ConfigTab">Settings tab to select when the step starts, or null.</param>
/// <param name="TryIt">Optional small task ("Try it: ..."): shown with a check mark once <see cref="Done"/> is true.</param>
/// <param name="Done">Condition for <see cref="TryIt"/>.</param>
public sealed record TourStep(
    string Title,
    string Body,
    TourWindow Window = TourWindow.None,
    string? Anchor = null,
    string? ConfigTab = null,
    string? TryIt = null,
    Func<Plugin, bool>? Done = null);

public sealed record Tour(string Id, string Name, IReadOnlyList<TourStep> Steps);

/// <summary>One-time contextual hint, shown the first time something happens.</summary>
public sealed record Tip(string Id, string Title, string Body, TourWindow Window = TourWindow.None, string? Anchor = null,
                         string? OfferTour = null);

/// <summary>All the texts of the guided tours and tips.</summary>
public static class TourContent
{
    public const string MainTourId = "main";
    public const string SettingsTourId = "settings";

    public static readonly Tour MainTour = new(MainTourId, "LinguaBridge window", new TourStep[]
    {
        new("Welcome to LinguaBridge!",
            "This short tour shows you around the main window (/lb). It takes about a minute.\n\n" +
            "Use Next / Back to move, or \"Skip tour\" at any time. You can restart it later with /lb tour " +
            "or from Settings > Help.",
            TourWindow.Main, "main.window"),
        new("Translate chat on / off",
            "When this switch is on (\"Translating\"), messages in the chats you selected are translated automatically and a translated " +
            "line appears in your chat log, with a tag like [EN>FR]. Only you see it.\n\n" +
            "The \"LB\" entry at the top right of the screen does the same (left click), and right click opens this window.",
            TourWindow.Main, "main.toggle"),
        new("Roleplay mode",
            "Turn this on for RP: emotes, *actions*, \"speech\" and ((OOC)) are kept intact, long posts are split " +
            "into several chat lines and DeepL / AI get roleplay context.\n\n/lb rp toggles it from the chat.",
            TourWindow.Main, "main.rp"),
        new("Your language pair",
            "The two languages you translate between, and the engine in use (hover it for details). Change them in Settings > Languages " +
            "and Settings > Engine.",
            TourWindow.Main, "main.pair"),
        new("Translation history",
            "Every translation lands here, newest on top:\n" +
            "- amber edge and down arrow = received, blue edge and up arrow = sent by you\n" +
            "- the dimmer line is the original text\n" +
            "- the copy icon copies the translation\n" +
            "- the reply icon answers that person translated, in the same channel (party, tell, FC...)\n" +
            "- the broom at the top clears the history (it asks first)",
            TourWindow.Main, "main.history"),
        new("Send or just translate",
            "\"Send to chat\": what you type is translated and sent to the game chat.\n\n" +
            "\"Translate only\": nothing is sent, the translation is copied so you can paste it anywhere " +
            "(Aetherphone's ChocoChat or Velvet, Discord, a website...).",
            TourWindow.Main, "main.mode"),
        new("Type your message",
            "Write in your own language and press Enter. LinguaBridge detects the language and translates into the " +
            "other language of your pair.",
            TourWindow.Main, "main.input",
            TryIt: "Type a short message here (it is not sent yet).",
            Done: p => p.MainWindowHasInput),
        new("Target language",
            "\"Auto\" translates into the other language of your pair. Pick a language here to force it, e.g. when a " +
            "friend writes in a third language.",
            TourWindow.Main, "main.target"),
        new("Where it is sent",
            "The chat channel the message goes to. \"Echo\" only shows it to you: perfect for testing.\n" +
            "(This list is hidden in \"Translate only\" mode and while replying.)",
            TourWindow.Main, "main.channel"),
        new("Preview before sending",
            "Preview translates without sending, so you can check it first (\"Check the meaning\" translates it back into " +
            "your language). Send then sends exactly what you previewed, or translates and sends in one go.",
            TourWindow.Main, "main.preview",
            TryIt: "Press Preview to see the translation of what you typed.",
            Done: p => p.MainWindowHasPreview),
        new("Even faster: /tr in the chat",
            "You don't need this window to send translated messages. In the normal chat box type:\n\n" +
            "  /tr Hello everyone!          -> current channel\n" +
            "  /tr /p Ready for the pull?   -> party\n" +
            "  /tr /t Name@World thanks!    -> tell\n" +
            "  /tr :en /fc Merci            -> force English\n\n" +
            "Other handy ones: /lb clip translates what you copied, and in Party Finder right-click a listing > " +
            "\"Translate description\"."),
        new("Settings",
            "Everything else (engine and DeepL keys, channels, how translations are shown, roleplay, AI, glossary) " +
            "is in Settings (the cog icon). The question-mark icon next to it restarts this tour.\n\nThat's it for this window! Do you want a quick look at the settings too?",
            TourWindow.Main, "main.settings"),
    });

    public static readonly Tour SettingsTour = new(SettingsTourId, "Settings", new TourStep[]
    {
        new("Engine",
            "Choose who translates: DeepL (best quality, free API key with 500,000 characters a month), Google " +
            "(no setup) or AI models. You can add several DeepL keys: when one runs out, the next is used, and " +
            "Google can take over automatically if DeepL fails.",
            TourWindow.Config, "config.tab.Engine", "Engine"),
        new("Languages",
            "Your language pair, the language you read best (incoming chat is translated into it), words that must " +
            "never be translated (names, FC...) and an optional learning language shown as an extra line.",
            TourWindow.Config, "config.tab.Languages", "Languages"),
        new("Chat",
            "Which channels are translated, whose messages, and how translations look: below the original or " +
            "replacing it, tag colour, short tag. Also the server bar switch, Party Finder menu and Aetherphone options.",
            TourWindow.Config, "config.tab.Chat", "Chat"),
        new("Roleplay",
            "Roleplay mode options: formality, a description of your scene and characters (sent as context, so " +
            "genders and tone come out right), names that are never translated and how long posts are split.",
            TourWindow.Config, "config.tab.Roleplay", "Roleplay"),
        new("AI",
            "Optional: use AI models (Mistral, Groq, Gemini, a local model...) for more natural translations, only " +
            "in roleplay or always. Several free providers are ready to use; DeepL / Google stay as backup.",
            TourWindow.Config, "config.tab.AI", "AI"),
        new("Glossary",
            "Your own translations for specific terms (DeepL): e.g. always translate \"tank\" as \"tank\". " +
            "\"Import recommended terms\" adds common game vocabulary.",
            TourWindow.Config, "config.tab.Glossary", "Glossary"),
        new("Help",
            "A cheat sheet with every command, the setup wizard, and the tutorial options: restart a tour, show the " +
            "tips again or turn them off.\n\nYou're all set. Happy chatting!",
            TourWindow.Config, "config.help.tutorial", "Help"),
    });

    public static readonly IReadOnlyList<Tour> Tours = new[] { MainTour, SettingsTour };

    // ---------------------------------------------------------------- contextual tips

    public static readonly Tip OfferMainTour = new("offer.main", "New here?",
        "Want a one-minute guided tour of this window? It shows what every part does.",
        TourWindow.Main, OfferTour: MainTourId);

    public static readonly Tip OfferSettingsTour = new("offer.settings", "Lots of options?",
        "Want a quick tour of the settings tabs? Seven short bubbles, one per tab.",
        TourWindow.Config, OfferTour: SettingsTourId);

    public static readonly Tip FirstTranslation = new("first.translation", "Your first translation!",
        "The line with the [xx>yy] tag in your chat is the translation of the message above it. Only you see it.\n\n" +
        "To answer translated, type /tr followed by your message, or press the reply icon on that message in /lb.");

    public static readonly Tip FirstSend = new("first.send", "Sent translated",
        "Your message went out translated. Your original text is shown in Echo (only to you) so you remember what " +
        "you wrote.\n\nNot sure about the meaning? In /lb, \"Preview\" then \"Check the meaning\" shows it before sending.");

    public static readonly Tip FirstReply = new("first.reply", "Replying",
        "Your next message goes to the same channel (and person, for tells) as the message you clicked. " +
        "The X next to \"Replying to...\" goes back to the channel list.",
        TourWindow.Main, "main.input");

    public static readonly Tip FirstRoleplay = new("first.roleplay", "Roleplay mode is on",
        "Emotes, *actions*, \"speech\" and ((OOC)) are handled for RP, the compose box gets bigger and long posts " +
        "are split automatically.\n\nTip: describe your scene and characters in Settings > Roleplay so genders " +
        "and tone are translated right.");

    public static readonly Tip FirstTranslateOnly = new("first.translateonly", "Translate only",
        "Nothing is sent to the game now: press Enter and the translation is copied to your clipboard, ready to " +
        "paste in ChocoChat, Velvet, Discord...\n\"Paste & translate\" does the opposite: translates what you copied.",
        TourWindow.Main, "main.mode");

    public static readonly Tip FirstPartyFinder = new("first.pf", "Party Finder translated",
        "The translation was printed in your chat with a [PF>..] tag and saved in the /lb history.");

    public static readonly Tip FirstFallback = new("first.fallback", "Backup engine in use",
        "Your main engine failed (out of quota, network or a bad key), so Google translated this one instead.\n\n" +
        "Check Settings > Engine: you can see your DeepL usage there and add more keys.");
}
