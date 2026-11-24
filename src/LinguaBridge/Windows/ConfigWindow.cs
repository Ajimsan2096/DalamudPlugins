using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using LinguaBridge.Translation;
using LinguaBridge.Tutorial;

namespace LinguaBridge.Windows;

public sealed class ConfigWindow : Window
{
    private static readonly (string Label, XivChatType[] Types)[] ChannelGroups =
    {
        ("Say", new[] { XivChatType.Say }),
        ("Yell", new[] { XivChatType.Yell }),
        ("Shout", new[] { XivChatType.Shout }),
        ("Party", new[] { XivChatType.Party, XivChatType.CrossParty }),
        ("Alliance", new[] { XivChatType.Alliance }),
        ("Tells (received)", new[] { XivChatType.TellIncoming }),
        ("Free Company", new[] { XivChatType.FreeCompany }),
        ("Linkshells 1-8", Enumerable.Range(0, 8).Select(i => (XivChatType)((int)XivChatType.Ls1 + i)).ToArray()),
        ("Cross-world LS 1-8",
            new[] { XivChatType.CrossLinkShell1 }
                .Concat(Enumerable.Range(0, 7).Select(i => (XivChatType)((int)XivChatType.CrossLinkShell2 + i))).ToArray()),
        ("Emotes (/em, always on in RP)", new[] { XivChatType.CustomEmote }),
        ("Novice Network", new[] { XivChatType.NoviceNetwork }),
        ("PvP Team", new[] { XivChatType.PvPTeam }),
    };

    private static readonly (string Label, string Value)[] Formalities =
    {
        ("Informal (tu)", "prefer_less"), ("Formal (vous)", "prefer_more"), ("Engine default", "default"),
    };

    private static readonly XivChatType[] OutputChannels =
        { XivChatType.Echo, XivChatType.Debug, XivChatType.SystemMessage, XivChatType.Notice };

    private readonly Plugin plugin;
    private bool showKey;
    private string newKey = string.Empty;
    private int presetIndex;
    private readonly ConcurrentDictionary<string, string> aiTestResults = new(); // written from background tasks
    private readonly ConcurrentDictionary<string, List<string>> aiModels = new();
    private string aiPrompt = string.Empty;
    private bool aiPromptLoaded;
    private string sceneNotes = string.Empty;
    private bool sceneNotesLoaded;
    private int glossaryFrom;
    private int glossaryTo = 1;
    private string newSource = string.Empty;
    private string newTarget = string.Empty;
    private string glossaryFilter = string.Empty;
    private string importResult = string.Empty;
    private bool checkingUsage;
    private string whitelistText;
    private string protectedText;
    private string learningPlayersText;

    public ConfigWindow(Plugin plugin) : base("LinguaBridge Settings###LinguaBridgeConfig")
    {
        this.plugin = plugin;
        Size = new Vector2(580, 620);
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(440, 360), MaximumSize = new Vector2(4000, 4000) };
        SizeCondition = ImGuiCond.FirstUseEver;
        whitelistText = string.Join(", ", plugin.Configuration.PlayerWhitelist);
        protectedText = string.Join(", ", plugin.Configuration.ProtectedTerms);
        learningPlayersText = string.Join(", ", plugin.Configuration.LearningPlayers);
    }

    public override void OnOpen() => plugin.Tutorial.ShowTip(TourContent.OfferSettingsTour);

    public override void Draw()
    {
        plugin.Tutorial.AnchorWindow("config.window");
        using var tabs = ImRaii.TabBar("tabs");
        if (!tabs)
            return;
        using (var tab = ImRaii.TabItem("Engine", TabFlags("Engine")))
        {
            plugin.Tutorial.Anchor("config.tab.Engine");
            if (tab) Body(DrawEngine);
        }
        using (var tab = ImRaii.TabItem("Languages", TabFlags("Languages")))
        {
            plugin.Tutorial.Anchor("config.tab.Languages");
            if (tab) Body(DrawLanguages);
        }
        using (var tab = ImRaii.TabItem("Chat", TabFlags("Chat")))
        {
            plugin.Tutorial.Anchor("config.tab.Chat");
            if (tab) Body(DrawChat);
        }
        using (var tab = ImRaii.TabItem("Roleplay", TabFlags("Roleplay")))
        {
            plugin.Tutorial.Anchor("config.tab.Roleplay");
            if (tab) Body(DrawRoleplay);
        }
        using (var tab = ImRaii.TabItem("AI", TabFlags("AI")))
        {
            plugin.Tutorial.Anchor("config.tab.AI");
            if (tab) Body(DrawAi);
        }
        using (var tab = ImRaii.TabItem("Glossary", TabFlags("Glossary")))
        {
            plugin.Tutorial.Anchor("config.tab.Glossary");
            if (tab) Body(DrawGlossary);
        }
        using (var tab = ImRaii.TabItem("Help", TabFlags("Help")))
        {
            plugin.Tutorial.Anchor("config.tab.Help");
            if (tab) Body(DrawHelp);
        }
    }

    /// <summary>Tab content in its own scrolling area, so the tab bar always stays visible.</summary>
    private static void Body(Action draw)
    {
        ImGui.Dummy(Ui.S(0, 2));
        using var child = ImRaii.Child("##body", Vector2.Zero, false);
        if (!child)
            return;
        ImGui.Dummy(Ui.S(0, 2));
        using (ImRaii.PushIndent(Ui.S(4)))
            draw();
        ImGui.Dummy(Ui.S(0, 8));
    }

    /// <summary>Lets the guided tour switch to a tab.</summary>
    private ImGuiTabItemFlags TabFlags(string label) =>
        plugin.Tutorial.ConsumeTabRequest(label) ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;

    private void DrawEngine()
    {
        var cfg = plugin.Configuration;
        var changed = false;

        Ui.Section("Translation engine", FontAwesomeIcon.Language, first: true);
        var engine = (int)cfg.Engine;
        string[] engines = { "DeepL (best quality, free API key)", "Google Translate (no key needed)" };
        ImGui.SetNextItemWidth(-1);
        if (ImGui.Combo("##engine", ref engine, engines, engines.Length))
        {
            cfg.Engine = (TranslationEngine)engine;
            plugin.Translator.ClearCache();
            changed = true;
        }

        if (cfg.Engine == TranslationEngine.DeepL)
        {
            Ui.Section("DeepL API keys", FontAwesomeIcon.Key,
                   help: "Keys are used in order. When one runs out of its monthly quota (or is invalid), the " +
                                       "next one is used automatically for the same message. Google is only used when " +
                                       "all keys fail (if the fallback below is on).");

            var deepl = plugin.Translator.DeepL;
            var keys = cfg.DeepLApiKeys;
            var remove = -1;
            var moveUp = -1;
            using (var table = ImRaii.Table("keys", 3, ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp))
            {
                if (table)
                {
                    ImGui.TableSetupColumn("key", ImGuiTableColumnFlags.WidthFixed, Ui.S(190));
                    ImGui.TableSetupColumn("status", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableSetupColumn("actions", ImGuiTableColumnFlags.WidthFixed, Ui.S(64));
                    for (var i = 0; i < keys.Count; i++)
                    {
                        using var id = ImRaii.PushId(i);
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGui.AlignTextToFramePadding();
                        ImGui.TextUnformatted($"#{i + 1}  {(showKey ? keys[i] : DeepLTranslator.Mask(keys[i]))}");
                        ImGui.TableNextColumn();
                        ImGui.AlignTextToFramePadding();
                        Ui.MutedText(deepl.Describe(keys[i]));
                        ImGui.TableNextColumn();
                        using (ImRaii.Disabled(i == 0))
                        {
                            if (Ui.GhostIconButton("up", FontAwesomeIcon.ArrowUp, "Use this key earlier"))
                                moveUp = i;
                        }
                        ImGui.SameLine();
                        if (Ui.GhostIconButton("rm", FontAwesomeIcon.TrashAlt, "Remove this key", Ui.Danger))
                            remove = i;
                    }
                }
            }
            if (keys.Count == 0)
                Ui.Status("No key yet: translations use Google until you add one.", StatusKind.Warning);
            if (remove >= 0)
            {
                keys.RemoveAt(remove);
                deepl.ResetKeyStates();
                changed = true;
            }
            if (moveUp > 0)
            {
                (keys[moveUp - 1], keys[moveUp]) = (keys[moveUp], keys[moveUp - 1]);
                deepl.ResetKeyStates();
                changed = true;
            }

            ImGui.SetNextItemWidth(-(Ui.IconTextButtonWidth(FontAwesomeIcon.Plus, "Add key") + ImGui.GetFrameHeight() +
                                     ImGui.CalcTextSize("Show").X + ImGui.GetStyle().ItemSpacing.X * 3 + ImGui.GetStyle().ItemInnerSpacing.X));
            ImGui.InputTextWithHint("##newkey", "Paste a DeepL API key (ends in :fx for Free)", ref newKey, 128,
                                    showKey ? ImGuiInputTextFlags.None : ImGuiInputTextFlags.Password);
            ImGui.SameLine();
            using (ImRaii.Disabled(string.IsNullOrWhiteSpace(newKey)))
            {
                if (Ui.Button(FontAwesomeIcon.Plus, "Add key", primary: true))
                {
                    var k = newKey.Trim();
                    if (!keys.Contains(k))
                        keys.Add(k);
                    newKey = string.Empty;
                    deepl.ResetKeyStates();
                    changed = true;
                    _ = deepl.GetUsageAsync(k, default); // show status/usage right away
                }
            }
            ImGui.SameLine();
            ImGui.Checkbox("Show", ref showKey);

            using (ImRaii.Disabled(checkingUsage || keys.Count == 0))
            {
                if (Ui.Button(FontAwesomeIcon.SyncAlt, "Check usage"))
                {
                    checkingUsage = true;
                    _ = deepl.RefreshAllUsageAsync(default).ContinueWith(_ => checkingUsage = false);
                }
            }
            ImGui.SameLine();
            if (Ui.Button(FontAwesomeIcon.Redo, "Retry used-up keys",
                          tooltip: "Keys that ran out are skipped for a few hours. Use this after your monthly quota resets."))
                deepl.ResetKeyStates();

            Ui.Hint("Free plan: 500,000 characters/month per account (deepl.com > API > \"DeepL API Free\"). " +
                              "Keys are stored in plain text in your Dalamud plugin config folder.");

            Ui.Section("DeepL options", FontAwesomeIcon.Cog);
            var f = Array.FindIndex(Formalities, x => x.Value == cfg.DeepLFormality);
            if (f < 0) f = 0;
            ImGui.SetNextItemWidth(Ui.S(200));
            if (ImGui.Combo("Formality (FR/DE/ES/IT/PT/JA/RU)", ref f, Formalities.Select(x => x.Label).ToArray(), Formalities.Length))
            {
                cfg.DeepLFormality = Formalities[f].Value;
                plugin.Translator.ClearCache();
                changed = true;
            }

            var gb = cfg.EnglishVariant == "EN-GB";
            if (ImGui.Checkbox("British English output (instead of American)", ref gb))
            {
                cfg.EnglishVariant = gb ? "EN-GB" : "EN-US";
                plugin.Translator.ClearCache();
                changed = true;
            }
        }
        if (cfg.Engine == TranslationEngine.DeepL)
        {
            var fb = cfg.FallbackToGoogle;
            if (ImGui.Checkbox("Use Google automatically if DeepL fails", ref fb))
            {
                cfg.FallbackToGoogle = fb;
                changed = true;
            }
            ImGuiComponents.HelpMarker("Quota used up, network error or wrong key: the message is translated with Google " +
                                       "instead of being lost. You get one warning in chat every 5 minutes at most.");
        }
        else
        {
            Ui.Hint("Google's free web endpoint works without setup, but it is unofficial, can rate-limit " +
                              "heavy use, and is a bit less natural for English/French than DeepL.");
        }

        ImGui.Dummy(Ui.S(0, 8));
        Ui.MutedText($"Characters sent this session: {plugin.Translator.CharactersSent:N0}");

        if (changed)
            plugin.SaveConfig();
    }

    private void DrawLanguages()
    {
        var cfg = plugin.Configuration;
        var names = Languages.All.Select(l => $"{l.Name} ({l.Code})").ToArray();
        var changed = false;

        Ui.Section("Language pair", FontAwesomeIcon.ExchangeAlt, first: true);
        var a = IndexOf(cfg.LanguageA);
        ImGui.SetNextItemWidth(Ui.FieldWidth);
        if (ImGui.Combo("Language A", ref a, names, names.Length))
        {
            cfg.LanguageA = Languages.All[a].Code;
            changed = true;
        }

        var b = IndexOf(cfg.LanguageB);
        ImGui.SetNextItemWidth(Ui.FieldWidth);
        if (ImGui.Combo("Language B", ref b, names, names.Length))
        {
            cfg.LanguageB = Languages.All[b].Code;
            changed = true;
        }

        if (cfg.LanguageA == cfg.LanguageB)
            Ui.Status("Language A and B must be different.", StatusKind.Error);
        Ui.Hint("Your messages sent with /tr go from A to B or from B to A automatically.");

        Ui.Section("Reading", FontAwesomeIcon.Book);
        var p = IndexOf(cfg.PreferredLanguage);
        ImGui.SetNextItemWidth(Ui.FieldWidth);
        if (ImGui.Combo("Language you read best", ref p, names, names.Length))
        {
            cfg.PreferredLanguage = Languages.All[p].Code;
            changed = true;
        }
        ImGuiComponents.HelpMarker("Incoming chat is translated into this language, plus Party Finder descriptions.");

        var oneWay = cfg.IncomingOnlyToPreferred;
        if (ImGui.Checkbox("Only translate incoming chat into the language I read best", ref oneWay))
        {
            cfg.IncomingOnlyToPreferred = oneWay;
            changed = true;
        }
        ImGuiComponents.HelpMarker("On: messages already in your language are left alone (e.g. reading English, only French " +
                                   "gets translated).\nOff: both directions (English is also translated to French). " +
                                   "/tr always works both ways.");

        var expand = cfg.ExpandAbbreviations;
        if (ImGui.Checkbox("Expand chat abbreviations before translating (jsp, tkt, idk, tbh...)", ref expand))
        {
            cfg.ExpandAbbreviations = expand;
            changed = true;
        }

        var others = cfg.TranslateOtherLanguages;
        if (ImGui.Checkbox("Also translate other languages (into the language you read best)", ref others))
        {
            cfg.TranslateOtherLanguages = others;
            changed = true;
        }

        Ui.Section("Never translate these words", FontAwesomeIcon.ShieldAlt);
        Ui.MutedText("Comma separated");
        ImGui.SameLine();
        ImGuiComponents.HelpMarker("Character names, nicknames, FC / static names, inside jokes... DeepL keeps them exactly " +
                                   "as written. Item links and auto-translate phrases are always kept automatically.");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputTextWithHint("##protected", "e.g. Rukenu, Les Chocobos, Moogle", ref protectedText, 2000))
        {
            cfg.ProtectedTerms = protectedText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            changed = true;
        }

        Ui.Section("Learning a language", FontAwesomeIcon.GraduationCap,
                   help: "Adds a second line under each translated message, in the language you are learning.\n" +
                                   "Example: you read Spanish, a German friend writes in German. You get the Spanish " +
                                   "translation as usual, plus the same message in English, to pick it up little by little.\n" +
                                   "Each learning line is one more request to the engine (DeepL characters).");
        var learningNames = new[] { "Off" }.Concat(names).ToArray();
        var l = string.IsNullOrWhiteSpace(cfg.LearningLanguage) || Languages.TryGet(cfg.LearningLanguage) == null
            ? 0
            : IndexOf(cfg.LearningLanguage) + 1;
        ImGui.SetNextItemWidth(Ui.FieldWidth);
        if (ImGui.Combo("Also show messages in", ref l, learningNames, learningNames.Length))
        {
            cfg.LearningLanguage = l == 0 ? string.Empty : Languages.All[l - 1].Code;
            changed = true;
        }

        if (!string.IsNullOrWhiteSpace(cfg.LearningLanguage))
        {
            using (ImRaii.PushIndent())
            {
                if (cfg.LearningLanguage == cfg.PreferredLanguage)
                    Ui.Status("This is the language you read best: messages are already translated into it.", StatusKind.Warning);

                ImGui.TextUnformatted("Only for these players (comma separated, empty = everyone):");
                ImGui.SetNextItemWidth(-1);
                if (ImGui.InputTextWithHint("##learningplayers", "e.g. Hanna Weber", ref learningPlayersText, 1000))
                {
                    cfg.LearningPlayers = learningPlayersText
                                          .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                    changed = true;
                }

                var untranslated = cfg.LearningForUntranslated;
                if (ImGui.Checkbox("Also for messages already in the language I read best", ref untranslated))
                {
                    cfg.LearningForUntranslated = untranslated;
                    changed = true;
                }
                ImGuiComponents.HelpMarker("Off: only messages that get translated also get the learning line.\n" +
                                           "On: messages written in your own language get it too (more engine requests).");

                int lcolor = cfg.LearningTagColor;
                ImGui.SetNextItemWidth(Ui.S(150));
                if (ImGui.InputInt("Learning tag colour (UIColor id)", ref lcolor))
                {
                    cfg.LearningTagColor = (ushort)Math.Clamp(lcolor, 0, 1000);
                    changed = true;
                }
            }
        }

        if (changed)
        {
            plugin.Translator.ClearCache();
            plugin.SaveConfig();
        }
    }

    private void DrawChat()
    {
        var cfg = plugin.Configuration;
        var changed = false;

        Ui.Section("Incoming chat", FontAwesomeIcon.Comments, first: true);
        var enabled = cfg.IncomingEnabled;
        if (ImGui.Checkbox("Translate incoming chat", ref enabled))
        {
            cfg.IncomingEnabled = enabled;
            changed = true;
        }

        Ui.Section("Channels to translate", FontAwesomeIcon.Comments);
        using (var table = ImRaii.Table("channels", 2, ImGuiTableFlags.SizingStretchSame))
        {
            if (!table)
                return;
            foreach (var (label, types) in ChannelGroups)
            {
                ImGui.TableNextColumn();
                var on = types.All(cfg.Channels.Contains);
                if (ImGui.Checkbox(label, ref on))
                {
                    foreach (var t in types)
                    {
                        if (on) cfg.Channels.Add(t);
                        else cfg.Channels.Remove(t);
                    }
                    changed = true;
                }
            }
        }

        Ui.Section("Filters", FontAwesomeIcon.Search);
        var own = cfg.TranslateOwnMessages;
        if (ImGui.Checkbox("Translate my own messages too", ref own))
        {
            cfg.TranslateOwnMessages = own;
            changed = true;
        }

        var combat = cfg.PauseInCombat;
        if (ImGui.Checkbox("Pause while in combat", ref combat))
        {
            cfg.PauseInCombat = combat;
            changed = true;
        }

        var spam = cfg.FilterMacroSpam;
        if (ImGui.Checkbox("Ignore macro spam", ref spam))
        {
            cfg.FilterMacroSpam = spam;
            changed = true;
        }
        ImGuiComponents.HelpMarker("Skips a player's lines when they arrive less than 0.65 s apart (multi-line macros), " +
                                   "saving DeepL characters.");

        ImGui.TextUnformatted("Only translate these players (comma separated, empty = everyone):");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##whitelist", ref whitelistText, 1000))
        {
            cfg.PlayerWhitelist = whitelistText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            changed = true;
        }

        var max = cfg.MaxMessageLength;
        ImGui.SetNextItemWidth(Ui.S(150));
        if (ImGui.SliderInt("Max message length", ref max, 20, 500))
        {
            cfg.MaxMessageLength = max;
            changed = true;
        }

        Ui.Section("Output", FontAwesomeIcon.Eye);

        string[] displays = { "Translation below the original", "Replace the original with its translation",
                              "Replace only in roleplay mode" };
        var display = (int)cfg.IncomingDisplay;
        ImGui.SetNextItemWidth(Ui.S(300));
        if (ImGui.Combo("How to show translations", ref display, displays, displays.Length))
        {
            cfg.IncomingDisplay = (IncomingDisplay)display;
            changed = true;
        }
        ImGuiComponents.HelpMarker("Replace: you only see the translated message (with its [FR>EN] tag), in the same order, " +
                                   "so long RP posts are not shown twice. If a translation fails or takes over 12 s, " +
                                   "the original is shown instead. Originals stay in the /lb history window.");

        var same = cfg.PrintInSameChannel;
        if (ImGui.Checkbox("Show translation in the same channel as the original", ref same))
        {
            cfg.PrintInSameChannel = same;
            changed = true;
        }
        if (!cfg.PrintInSameChannel)
        {
            var idx = Math.Max(0, Array.IndexOf(OutputChannels, cfg.FallbackOutputChannel));
            ImGui.SetNextItemWidth(Ui.S(150));
            if (ImGui.Combo("Output channel", ref idx, OutputChannels.Select(c => c.ToString()).ToArray(), OutputChannels.Length))
            {
                cfg.FallbackOutputChannel = OutputChannels[idx];
                changed = true;
            }
        }

        int color = cfg.TagColor;
        ImGui.SetNextItemWidth(Ui.S(150));
        if (ImGui.InputInt("Tag colour (UIColor id)", ref color))
        {
            cfg.TagColor = (ushort)Math.Clamp(color, 0, 1000);
            changed = true;
        }

        var shortTag = cfg.ShortTag;
        if (ImGui.Checkbox("Short tag: [FR] instead of [EN>FR]", ref shortTag))
        {
            cfg.ShortTag = shortTag;
            changed = true;
        }
        ImGuiComponents.HelpMarker("Takes less room, handy in narrow chat bubbles such as Aetherphone's Linkpearl.");

        var echo = cfg.EchoOriginalOnSend;
        if (ImGui.Checkbox("After /tr, show my original text in Echo", ref echo))
        {
            cfg.EchoOriginalOnSend = echo;
            changed = true;
        }

        DrawAetherphone(ref changed);

        Ui.Section("Extras", FontAwesomeIcon.Magic);

        var dtr = cfg.ShowDtrEntry;
        if (ImGui.Checkbox("Show \"LB\" on/off switch in the server info bar", ref dtr))
        {
            cfg.ShowDtrEntry = dtr;
            changed = true;
        }
        ImGuiComponents.HelpMarker("Top right of the screen. Left click toggles translation, right click opens the window.");

        var pf = cfg.PartyFinderMenu;
        if (ImGui.Checkbox("Party Finder: \"Translate description\" in the right-click menu", ref pf))
        {
            cfg.PartyFinderMenu = pf;
            changed = true;
        }

        if (changed)
            plugin.SaveConfig();
    }

    private void DrawRoleplay()
    {
        var cfg = plugin.Configuration;
        var changed = false;

        Ui.Section("Roleplay mode", FontAwesomeIcon.TheaterMasks, first: true);
        var rp = cfg.RoleplayMode;
        if (ImGui.Checkbox("Roleplay mode", ref rp))
            plugin.SetRoleplayMode(rp, announce: false);
        ImGui.SameLine();
        Ui.MutedText("(/lb rp toggles it)");
        Ui.Hint("When on:\n" +
                          " - DeepL is told it is translating in-character prose (tone, voice, mature scenes untouched).\n" +
                          " - *actions*, \"speech\" / « speech », ((OOC)) and [OOC ...] keep their markers.\n" +
                          " - Emotes (/em) are translated, using the character name as subject; /tr /em works too.\n" +
                          " - Multi-line posts are not dropped as macro spam, and posts up to 600 characters are read.\n" +
                          " - IC, OOC, RP, ERP... and character names stay untouched.");

        Ui.Section("Translation context", FontAwesomeIcon.Book);
        var f = Array.FindIndex(Formalities, x => x.Value == cfg.RpFormality);
        if (f < 0) f = 2;
        ImGui.SetNextItemWidth(Ui.S(200));
        if (ImGui.Combo("Formality in RP", ref f, Formalities.Select(x => x.Label).ToArray(), Formalities.Length))
        {
            cfg.RpFormality = Formalities[f].Value;
            plugin.Translator.ClearCache();
            changed = true;
        }
        ImGuiComponents.HelpMarker("\"Engine default\" lets DeepL pick tu/vous from the conversation context, which is " +
                                   "usually right in RP. Force \"vous\" for courtly / noble characters.");

        var lines = cfg.ContextLines;
        ImGui.SetNextItemWidth(Ui.S(200));
        if (ImGui.SliderInt("Conversation lines used as context", ref lines, 0, 10))
        {
            cfg.ContextLines = lines;
            changed = true;
        }
        ImGuiComponents.HelpMarker("The previous lines of the same chat (party, a tell, say + emotes...) are sent to DeepL " +
                                   "as context. DeepL does not bill context characters. It fixes pronouns, gender " +
                                   "agreement (il/elle, beau/belle), tu/vous and ambiguous words. Applies outside RP too.");

        var names = cfg.AutoProtectNames;
        if (ImGui.Checkbox("Never translate character names (sender and you)", ref names))
        {
            cfg.AutoProtectNames = names;
            changed = true;
        }

        if (!sceneNotesLoaded)
        {
            sceneNotes = cfg.RpSceneNotes;
            sceneNotesLoaded = true;
        }
        ImGui.Dummy(Ui.S(0, 4));
        ImGui.TextUnformatted("Scene notes for DeepL (optional, not billed)");
        ImGui.SameLine();
        ImGuiComponents.HelpMarker("Short facts that help the translation, in any language. Examples:\n" +
                                   "\"Setting: a quiet inn in Ul'dah at night. The characters are a couple, intimate and teasing. " +
                                   "Keep pet names (bebé, mon chéri, princess) as written.\"");
        if (ImGui.InputTextMultiline("##scene", ref sceneNotes, 600, new Vector2(-1, ImGui.GetTextLineHeight() * 4)))
        {
            cfg.RpSceneNotes = sceneNotes;
            changed = true;
        }

        DrawCharacters(ref changed);

        Ui.Section("Sending long posts", FontAwesomeIcon.PaperPlane);
        var split = cfg.SplitLongMessages;
        if (ImGui.Checkbox("Split translations longer than the chat limit into several lines", ref split))
        {
            cfg.SplitLongMessages = split;
            changed = true;
        }
        if (cfg.SplitLongMessages)
        {
            var markers = cfg.SplitMarkers;
            if (ImGui.Checkbox("Add (1/2), (2/2) markers", ref markers))
            {
                cfg.SplitMarkers = markers;
                changed = true;
            }
        }

        ImGui.Dummy(Ui.S(0, 10));
        Ui.Callout(FontAwesomeIcon.ShieldAlt, Ui.Warning, "Privacy", "Translated text is sent to DeepL or Google. According to DeepL, texts sent with the free API " +
                          "may be kept and used to improve their service, while DeepL Pro deletes them after translation. " +
                          "Google's free endpoint gives no guarantees. Keep that in mind for private or intimate scenes " +
                          "(you can pause translation with the LB switch or /lb off at any time).");

        if (changed)
            plugin.SaveConfig();
    }

    private void DrawCharacters(ref bool changed)
    {
        var cfg = plugin.Configuration;
        Ui.Section("Characters", FontAwesomeIcon.Book,
                   help: "Gender and short notes per character. Sent to DeepL (not billed) only when that " +
                                   "character is in the conversation. Fixes il/elle, sur/sure, beau/belle, he/she, " +
                                   "and helps with tone (\"they are a couple, very affectionate\"). Works outside RP too.");

        var me = Plugin.PlayerState.CharacterName;
        var quick = false;
        if (!string.IsNullOrEmpty(me) && !cfg.Characters.Any(c => c.Name.Equals(me, StringComparison.OrdinalIgnoreCase)))
        {
            quick = true;
            if (Ui.Button(FontAwesomeIcon.Plus, $"Add me ({me})"))
            {
                cfg.Characters.Add(new CharacterProfile { Name = me });
                changed = true;
            }
        }
        var last = plugin.History.FirstOrDefault(h => !h.Outgoing && h.Sender is not ("Listing" or "You"))?.Sender;
        if (last != null && !cfg.Characters.Any(c => c.Name.Equals(last, StringComparison.OrdinalIgnoreCase)))
        {
            if (quick)
                ImGui.SameLine();
            if (Ui.Button(FontAwesomeIcon.Plus, $"Add {last}", tooltip: "The last person who wrote to you"))
            {
                cfg.Characters.Add(new CharacterProfile { Name = last });
                changed = true;
            }
        }

        var remove = -1;
        using (var table = ImRaii.Table("chars", 4, ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp))
        {
            if (table)
            {
                ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthFixed, Ui.S(140));
                ImGui.TableSetupColumn("Gender", ImGuiTableColumnFlags.WidthFixed, Ui.S(105));
                ImGui.TableSetupColumn("Notes", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFrameHeight());
                ImGui.TableHeadersRow();
                for (var i = 0; i < cfg.Characters.Count; i++)
                {
                    var c = cfg.Characters[i];
                    using var id = ImRaii.PushId(i);
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    var name = c.Name;
                    ImGui.SetNextItemWidth(-1);
                    if (ImGui.InputText("##n", ref name, 64))
                    {
                        c.Name = name;
                        changed = true;
                    }
                    ImGui.TableNextColumn();
                    var g = Math.Max(0, Array.IndexOf(CharacterProfile.Genders, c.Gender));
                    ImGui.SetNextItemWidth(-1);
                    if (ImGui.Combo("##g", ref g, CharacterProfile.Genders, CharacterProfile.Genders.Length))
                    {
                        c.Gender = CharacterProfile.Genders[g];
                        changed = true;
                    }
                    ImGui.TableNextColumn();
                    var notes = c.Notes;
                    ImGui.SetNextItemWidth(-1);
                    if (ImGui.InputTextWithHint("##o", "e.g. her boyfriend, affectionate, calls her 'princess'", ref notes, 200))
                    {
                        c.Notes = notes;
                        changed = true;
                    }
                    ImGui.TableNextColumn();
                    if (Ui.GhostIconButton("rm", FontAwesomeIcon.TrashAlt, $"Remove {c.Name}", Ui.Danger))
                        remove = i;
                }
            }
        }
        if (Ui.Button(FontAwesomeIcon.Plus, "Add character"))
        {
            cfg.Characters.Add(new CharacterProfile());
            changed = true;
        }
        if (remove >= 0)
        {
            cfg.Characters.RemoveAt(remove);
            changed = true;
        }
        if (changed)
            plugin.Translator.ClearCache();
    }

    private void DrawAi()
    {
        var cfg = plugin.Configuration;
        var llm = plugin.Translator.Llm;
        var changed = false;

        Ui.Section("AI translation", FontAwesomeIcon.Robot, first: true);
        Ui.Hint("Use AI models (any OpenAI-compatible API) as translator. They understand idioms, tone and " +
                          "whole scenes much better than DeepL; best for roleplay. Providers are tried in order; " +
                          "if all fail, DeepL / Google are used.");

        string[] usages = { "Off", "Only in roleplay mode (recommended)", "Always" };
        var usage = (int)cfg.LlmUsage;
        ImGui.SetNextItemWidth(Ui.S(260));
        if (ImGui.Combo("When to use AI", ref usage, usages, usages.Length))
        {
            cfg.LlmUsage = (LlmUsage)usage;
            plugin.Translator.ClearCache();
            changed = true;
        }
        Ui.Status(plugin.Translator.UsingLlm ? "AI is active right now." :
                  llm.ActiveProviders.Count == 0 ? "No provider configured yet." : "AI is not active in the current mode.",
                  plugin.Translator.UsingLlm ? StatusKind.Success : llm.ActiveProviders.Count == 0 ? StatusKind.Warning : StatusKind.Info);

        Ui.Section("Providers (tried top to bottom)", FontAwesomeIcon.Robot);

        var remove = -1;
        var moveUp = -1;
        for (var i = 0; i < cfg.LlmProviders.Count; i++)
        {
            var p = cfg.LlmProviders[i];
            using var id = ImRaii.PushId(p.Id);
            var enabled = p.Enabled;
            if (ImGui.Checkbox("##en", ref enabled))
            {
                p.Enabled = enabled;
                changed = true;
            }
            ImGui.SameLine();
            var open = ImGui.CollapsingHeader($"#{i + 1} {p.Name}  [{p.Model}]  - {llm.Describe(p)}###hdr");
            if (!open)
                continue;

            using (ImRaii.PushIndent())
            {
                var name = p.Name;
                ImGui.SetNextItemWidth(Ui.S(200));
                if (ImGui.InputText("Name", ref name, 40)) { p.Name = name; changed = true; }
                var url = p.BaseUrl;
                ImGui.SetNextItemWidth(Ui.S(-90));
                if (ImGui.InputText("Base URL", ref url, 200)) { p.BaseUrl = url.Trim(); changed = true; }
                var key = p.ApiKey;
                ImGui.SetNextItemWidth(Ui.S(-90));
                if (ImGui.InputText("API key", ref key, 200, showKey ? ImGuiInputTextFlags.None : ImGuiInputTextFlags.Password))
                {
                    p.ApiKey = key.Trim();
                    changed = true;
                }
                var model = p.Model;
                ImGui.SetNextItemWidth(Ui.S(-200));
                if (ImGui.InputText("Model", ref model, 120)) { p.Model = model.Trim(); changed = true; }
                ImGui.SameLine();
                if (ImGui.SmallButton("List models"))
                {
                    aiTestResults[p.Id] = "Loading models...";
                    _ = llm.ListModelsAsync(p, default).ContinueWith(t =>
                    {
                        if (t.IsCompletedSuccessfully)
                        {
                            aiModels[p.Id] = t.Result;
                            aiTestResults[p.Id] = $"{t.Result.Count} models found";
                        }
                        else
                        {
                            aiTestResults[p.Id] = "Could not list models: " + t.Exception?.GetBaseException().Message;
                        }
                    });
                }
                if (aiModels.TryGetValue(p.Id, out var models) && models.Count > 0)
                {
                    var current = Math.Max(0, models.IndexOf(p.Model));
                    ImGui.SetNextItemWidth(Ui.S(-200));
                    if (ImGui.Combo("Pick model", ref current, models.ToArray(), models.Count))
                    {
                        p.Model = models[current];
                        changed = true;
                    }
                }

                if (Ui.Button(FontAwesomeIcon.Magic, "Test"))
                {
                    aiTestResults[p.Id] = "Testing...";
                    _ = llm.TestAsync(p, default).ContinueWith(t => aiTestResults[p.Id] = t.IsCompletedSuccessfully
                        ? t.Result : "Failed: " + t.Exception?.GetBaseException().Message);
                }
                ImGui.SameLine();
                using (ImRaii.Disabled(i == 0))
                    if (Ui.Button(FontAwesomeIcon.ArrowUp, "Move up"))
                        moveUp = i;
                ImGui.SameLine();
                if (Ui.Button(FontAwesomeIcon.TrashAlt, "Remove"))
                    remove = i;
                if (aiTestResults.TryGetValue(p.Id, out var res))
                    Ui.Status(res, res.StartsWith("Failed") || res.StartsWith("Could not") ? StatusKind.Error
                                 : res.EndsWith("...") ? StatusKind.Busy : StatusKind.Info);
                var preset = LlmProvider.Presets.FirstOrDefault(x => x.Url == p.BaseUrl);
                if (preset.Name != null)
                {
                    Ui.MutedText(preset.Note);
                    if (ImGui.SmallButton("Copy key page link"))
                        ImGui.SetClipboardText(preset.KeyPage);
                }
            }
            ImGui.Spacing();
        }
        if (remove >= 0)
        {
            cfg.LlmProviders.RemoveAt(remove);
            changed = true;
        }
        if (moveUp > 0)
        {
            (cfg.LlmProviders[moveUp - 1], cfg.LlmProviders[moveUp]) = (cfg.LlmProviders[moveUp], cfg.LlmProviders[moveUp - 1]);
            changed = true;
        }

        var presetNames = LlmProvider.Presets.Select(x => x.Name).Append("Custom (any OpenAI-compatible URL)").ToArray();
        ImGui.Dummy(Ui.S(0, 4));
        ImGui.SetNextItemWidth(Ui.S(260));
        ImGui.Combo("##preset", ref presetIndex, presetNames, presetNames.Length);
        ImGui.SameLine();
        if (Ui.Button(FontAwesomeIcon.Plus, "Add provider", primary: true))
        {
            var p = new LlmProvider();
            if (presetIndex < LlmProvider.Presets.Length)
            {
                var x = LlmProvider.Presets[presetIndex];
                p.Name = x.Name;
                p.BaseUrl = x.Url;
                p.Model = x.Model;
                ImGui.SetClipboardText(x.KeyPage);
                aiTestResults[p.Id] = $"Key page copied to clipboard: {x.KeyPage}  - paste your key above, then Test.";
            }
            cfg.LlmProviders.Add(p);
            changed = true;
        }
        ImGui.SameLine();
        ImGui.Checkbox("Show keys", ref showKey);
        if (Ui.Button(FontAwesomeIcon.Redo, "Retry paused providers"))
            llm.ResetStates();

        ImGui.Dummy(Ui.S(0, 8));
        if (ImGui.CollapsingHeader("Advanced: prompt and temperature"))
        {
            var temp = cfg.LlmTemperature;
            ImGui.SetNextItemWidth(Ui.S(150));
            if (ImGui.SliderFloat("Temperature", ref temp, 0f, 1f, "%.2f"))
            {
                cfg.LlmTemperature = temp;
                changed = true;
            }
            ImGuiComponents.HelpMarker("0.2-0.4 keeps translations faithful. Higher = freer, more creative wording.");
            if (!aiPromptLoaded)
            {
                aiPrompt = string.IsNullOrWhiteSpace(cfg.LlmPrompt) ? LlmTranslator.DefaultPrompt : cfg.LlmPrompt;
                aiPromptLoaded = true;
            }
            Ui.MutedText("{target} is replaced by the target language. Keep the [[xx]] output rule.");
            if (ImGui.InputTextMultiline("##prompt", ref aiPrompt, 4000, new Vector2(-1, ImGui.GetTextLineHeight() * 10)))
            {
                cfg.LlmPrompt = aiPrompt.Trim() == LlmTranslator.DefaultPrompt.Trim() ? string.Empty : aiPrompt;
                plugin.Translator.ClearCache();
                changed = true;
            }
            if (ImGui.SmallButton("Reset prompt"))
            {
                cfg.LlmPrompt = string.Empty;
                aiPrompt = LlmTranslator.DefaultPrompt;
                changed = true;
            }
        }

        ImGui.Dummy(Ui.S(0, 10));
        Ui.Callout(FontAwesomeIcon.ShieldAlt, Ui.Warning, "Privacy", "Messages (yours and your partners') are sent to the provider you pick. Free plans may store " +
                          "them or use them for training; check each provider's terms. A local model (Ollama / " +
                          "LM Studio) keeps everything on your PC. Keys are stored in plain text in your plugin config.");

        if (changed)
        {
            plugin.Translator.ClearCache();
            plugin.SaveConfig();
        }
    }

    private void DrawGlossary()
    {
        var cfg = plugin.Configuration;
        var names = Languages.All.Select(l => l.Code).ToArray();
        var changed = false;

        Ui.Section("Glossary", FontAwesomeIcon.Book, first: true);
        Ui.Hint("Your own translations for specific words: lore terms, nicknames, pet names, RP slang, " +
                          "how intimate or emotional words should be rendered... DeepL always uses these when it " +
                          "knows the source language. They are uploaded to your DeepL account as glossaries " +
                          "(one per direction and per key) automatically. DeepL only.");
        Ui.MutedText($"Status: {plugin.Translator.DeepL.GlossaryStatus}");
        if (Ui.Button(FontAwesomeIcon.Magic, "Import recommended terms"))
        {
            var added = 0;
            foreach (var (from, to, source, target) in PairStyle.RecommendedGlossary)
            {
                if (cfg.Glossary.Any(e => e.From == from && e.To == to && e.Source.Equals(source, StringComparison.OrdinalIgnoreCase)))
                    continue;
                cfg.Glossary.Add(new GlossaryEntry { From = from, To = to, Source = source, Target = target });
                added++;
            }
            importResult = added == 0 ? "Already imported." : $"{added} terms added.";
            changed = true;
        }
        ImGuiComponents.HelpMarker("Endearments (amor > mon amour, mi amor > mein Schatz, babe > bébé / Schatz, " +
                                   "mon cœur > sweetheart...) and official FFXIV job/term names between English and French " +
                                   "(Dark Knight <> Chevalier noir) and English and German (Dark Knight <> Dunkelritter, " +
                                   "Free Company <> Freie Gesellschaft...). " +
                                   "Remove any entry you don't like.");
        if (importResult.Length > 0)
        {
            ImGui.SameLine();
            Ui.MutedText(importResult);
        }

        Ui.Section("Add a term", FontAwesomeIcon.Plus);

        ImGui.SetNextItemWidth(Ui.S(60));
        ImGui.Combo("##gfrom", ref glossaryFrom, names, names.Length);
        ImGui.SameLine();
        ImGui.TextUnformatted(">");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(Ui.S(60));
        ImGui.Combo("##gto", ref glossaryTo, names, names.Length);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(Ui.S(150));
        ImGui.InputTextWithHint("##gsrc", "word or phrase", ref newSource, 100);
        ImGui.SameLine();
        ImGui.TextUnformatted("=");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(Ui.S(150));
        ImGui.InputTextWithHint("##gdst", "translation", ref newTarget, 100);
        ImGui.SameLine();
        using (ImRaii.Disabled(string.IsNullOrWhiteSpace(newSource) || string.IsNullOrWhiteSpace(newTarget) ||
                               glossaryFrom == glossaryTo))
        {
            if (Ui.Button(FontAwesomeIcon.Plus, "Add", primary: true))
            {
                cfg.Glossary.RemoveAll(e => e.From == names[glossaryFrom] && e.To == names[glossaryTo] &&
                                            e.Source.Equals(newSource.Trim(), StringComparison.Ordinal));
                cfg.Glossary.Add(new GlossaryEntry
                {
                    From = names[glossaryFrom], To = names[glossaryTo], Source = newSource.Trim(), Target = newTarget.Trim(),
                });
                newSource = newTarget = string.Empty;
                changed = true;
            }
        }
        ImGui.SameLine();
        if (Ui.IconButton("swap", FontAwesomeIcon.ExchangeAlt,
                          "Swap the direction. Each direction is a separate glossary: add EN>FR and FR>EN entries separately."))
            (glossaryFrom, glossaryTo) = (glossaryTo, glossaryFrom);
        if (glossaryFrom == glossaryTo)
            Ui.Status("Pick two different languages.", StatusKind.Warning);

        Ui.Section("Terms", FontAwesomeIcon.Search);
        Ui.Icon(FontAwesomeIcon.Search, Ui.Muted);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(Ui.S(200));
        ImGui.InputTextWithHint("##gfilter", "filter...", ref glossaryFilter, 50);
        ImGui.SameLine();
        Ui.MutedText($"{cfg.Glossary.Count} entries");

        var remove = -1;
        using (var table = ImRaii.Table("glossary", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.BordersInnerH,
                                        new Vector2(-1, Math.Max(ImGui.GetContentRegionAvail().Y, ImGui.GetFrameHeight() * 8))))
        {
            if (table)
            {
                ImGui.TableSetupColumn("Dir", ImGuiTableColumnFlags.WidthFixed, Ui.S(60));
                ImGui.TableSetupColumn("Source");
                ImGui.TableSetupColumn("Translation");
                ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFrameHeight());
                ImGui.TableSetupScrollFreeze(0, 1);
                ImGui.TableHeadersRow();
                for (var i = 0; i < cfg.Glossary.Count; i++)
                {
                    var e = cfg.Glossary[i];
                    if (glossaryFilter.Length > 0 &&
                        !e.Source.Contains(glossaryFilter, StringComparison.OrdinalIgnoreCase) &&
                        !e.Target.Contains(glossaryFilter, StringComparison.OrdinalIgnoreCase))
                        continue;
                    using var id = ImRaii.PushId(i);
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextColored(Ui.Accent, $"{e.From} > {e.To}");
                    ImGui.TableNextColumn();
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextUnformatted(e.Source);
                    ImGui.TableNextColumn();
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextUnformatted(e.Target);
                    ImGui.TableNextColumn();
                    if (Ui.GhostIconButton("rm", FontAwesomeIcon.TrashAlt, $"Remove \"{e.Source}\"", Ui.Danger))
                        remove = i;
                }
            }
        }
        if (remove >= 0)
        {
            cfg.Glossary.RemoveAt(remove);
            changed = true;
        }

        if (changed)
        {
            plugin.Translator.ClearCache();
            plugin.SaveConfig();
        }
    }

    private void DrawAetherphone(ref bool changed)
    {
        var cfg = plugin.Configuration;
        var detected = plugin.AetherphoneLoaded;
        Ui.Section("Aetherphone (Linkpearl)", FontAwesomeIcon.Comments);
        if (detected)
        {
            ImGui.SameLine();
            Ui.Badge("detected", Ui.Success);
        }

        var handled = cfg.TranslateHandledMessages;
        if (ImGui.Checkbox("Translate messages other plugins hid from the chat log", ref handled))
        {
            cfg.TranslateHandledMessages = handled;
            changed = true;
        }
        ImGuiComponents.HelpMarker("Turn this on if a Linkpearl channel is set to \"hide from game chat\": otherwise " +
                                   "those messages are never translated. The translation is printed in the chat log " +
                                   "(that is how it reaches Linkpearl), the original stays hidden.\n" +
                                   "Careful: spam filters hide messages too, and their translations would show up.");

        if (!detected)
            return;
        using (ImRaii.PushColor(ImGuiCol.Text, Ui.Muted))
        {
            ImGui.TextWrapped(
                "- Linkpearl shows the translations as a second message from the same player, also in Replace mode " +
                "(Aetherphone keeps the original). \"Below the original\" + short tag reads best there.\n" +
                "- Send translated from Linkpearl's composer with /tr and the channel: /tr /p hello, " +
                "/tr /t Name@World hello (without a channel it goes to the game's current channel, not the Linkpearl tab).\n" +
                "- ChocoChat and Velvet are not game chat: use the main window in \"Translate only\" or /lb clip.");
        }
    }

    private static readonly (string Command, string What)[] Commands =
    {
        ("/tr Hello everyone!", "Translate and send to the current channel"),
        ("/tr /p Ready for the pull?", "... to party (also /fc, /l1, /cwl1, /em...)"),
        ("/tr /t Jane Doe@Ragnarok hi!", "... as a tell"),
        ("/tr /t <t> thanks!", "... as a tell to your target"),
        ("/tr :en /fc Merci beaucoup", "Force the target language"),
        ("/lb", "History window: Reply buttons, preview before sending"),
        ("/lb clip  (/lb clip :fr)", "Translate what you copied, copy the result back"),
        ("/lb on | off | toggle", "Switch incoming translation"),
        ("/lb rp", "Toggle roleplay mode"),
        ("/lb config", "These settings"),
        ("/lb wizard", "Run the setup again"),
        ("/lb tour  (/lb tour settings)", "Guided tour"),
        ("/lb tips on | off | reset", "Contextual tips"),
    };

    private void DrawHelp()
    {
        Ui.Section("Getting started", FontAwesomeIcon.Flag, first: true);
        if (Ui.Button(FontAwesomeIcon.Magic, "Open setup wizard"))
            plugin.OpenWizard();
        DrawTutorialOptions();

        Ui.Section("How it works", FontAwesomeIcon.InfoCircle);
        ImGui.TextWrapped("Incoming: messages in the selected channels are translated automatically and shown below the " +
                          "original with a tag like [EN>FR]. Only you see the translation.");
        ImGui.Dummy(Ui.S(0, 2));
        ImGui.TextWrapped("Outgoing: type /tr followed by your message. It is translated to the other language of the " +
                          "pair and sent to your current chat channel.");

        Ui.Section("Commands", FontAwesomeIcon.Book);
        using (var table = ImRaii.Table("commands", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.PadOuterX))
        {
            if (table)
            {
                ImGui.TableSetupColumn("Command", ImGuiTableColumnFlags.WidthFixed, Ui.S(220));
                ImGui.TableSetupColumn("What it does", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableHeadersRow();
                foreach (var (cmd, what) in Commands)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.TextColored(Ui.Accent, cmd);
                    if (ImGui.IsItemClicked())
                        ImGui.SetClipboardText(cmd);
                    Ui.Tooltip("Click to copy");
                    ImGui.TableNextColumn();
                    ImGui.TextWrapped(what);
                }
            }
        }

        Ui.Section("Good to know", FontAwesomeIcon.InfoCircle);
        Ui.Hint("- Party Finder: right-click a listing > \"Translate description\".");
        Ui.Hint("- Translate only (nothing sent): in /lb pick \"Translate only\", or copy any text and type /lb clip. " +
                "The translation is copied, ready to paste in Aetherphone's ChocoChat or Velvet, Discord or a website.");
        Ui.Hint("- Aetherphone / Linkpearl: translations show up there too (as a second message). Use /tr /p ..., " +
                "/tr /t Name@World ... in its composer to send translated. See Chat > Aetherphone.");
        Ui.Hint("- Your friends need the plugin too to read your messages in their language, or simply use /tr so " +
                "they receive your text already translated.");
    }

    private void DrawTutorialOptions()
    {
        var cfg = plugin.Configuration;
        ImGui.Dummy(Ui.S(0, 4));
        using (ImRaii.Group())
        {
            ImGui.TextUnformatted("Guided tour and tips");
            if (Ui.Button(FontAwesomeIcon.Flag, "Tour of the main window"))
                plugin.Tutorial.StartTour(TourContent.MainTourId);
            ImGui.SameLine();
            if (Ui.Button(FontAwesomeIcon.Cog, "Tour of the settings"))
                plugin.Tutorial.StartTour(TourContent.SettingsTourId);
            var tips = cfg.TipsEnabled;
            if (ImGui.Checkbox("Show tips the first time I use a feature", ref tips))
                plugin.Tutorial.SetTipsEnabled(tips);
            ImGui.SameLine();
            if (Ui.Button(FontAwesomeIcon.Redo, "Show all tips again",
                          tooltip: "Every tip and tour offer will appear again when it makes sense."))
                plugin.Tutorial.ResetTips();
            Ui.MutedText("Also: /lb tour, /lb tour settings, /lb tips on|off|reset");
        }
        plugin.Tutorial.Anchor("config.help.tutorial");
    }

    private static int IndexOf(string code)
    {
        var i = Languages.All.ToList().FindIndex(l => l.Code == code);
        return i < 0 ? 0 : i;
    }
}
