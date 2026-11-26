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
using static LinguaBridge.Localization.Loc;

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
        ("Informal (tu / du / tú)", "prefer_less"), ("Formal (vous / Sie / usted)", "prefer_more"), ("Engine default", "default"),
    };

    private static readonly (XivChatType Type, string Label)[] OutputChannels =
    {
        (XivChatType.Echo, "Echo"), (XivChatType.Debug, "Debug"), (XivChatType.SystemMessage, "System messages"),
        (XivChatType.Notice, "Notice"),
    };

    private readonly Plugin plugin;
    private bool showKey;
    private string newKey = string.Empty;
    private int presetIndex;
    private readonly ConcurrentDictionary<string, (string Text, StatusKind Kind)> aiTestResults = new(); // written from background tasks
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
    private string neverPlayersText;
    private string alwaysPlayersText;

    public ConfigWindow(Plugin plugin) : base("LinguaBridge Settings###LinguaBridgeConfig")
    {
        this.plugin = plugin;
        Size = new Vector2(580, 620);
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(440, 360), MaximumSize = new Vector2(4000, 4000) };
        SizeCondition = ImGuiCond.FirstUseEver;
        whitelistText = string.Join(", ", plugin.Configuration.PlayerWhitelist);
        protectedText = string.Join(", ", plugin.Configuration.ProtectedTerms);
        learningPlayersText = string.Join(", ", plugin.Configuration.LearningPlayers);
        neverPlayersText = string.Join(", ", plugin.Configuration.NeverTranslatePlayers);
        alwaysPlayersText = string.Join(", ", plugin.Configuration.AlwaysTranslatePlayers);
    }

    private static List<string> SplitList(string text) =>
        text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    /// <summary>True when the text field above was just left after editing: save then, not on every key press.</summary>
    private static bool Committed() => ImGui.IsItemDeactivatedAfterEdit();

    public override void OnOpen() => plugin.Tutorial.ShowTip(TourContent.OfferSettingsTour);

    public override void PreDraw() => WindowName = L("LinguaBridge Settings") + "###LinguaBridgeConfig";

    public override void Draw()
    {
        plugin.Tutorial.AnchorWindow("config.window");
        using var tabs = ImRaii.TabBar("tabs");
        if (!tabs)
            return;
        using (var tab = ImRaii.TabItem(TabLabel("Engine"), TabFlags("Engine")))
        {
            plugin.Tutorial.Anchor("config.tab.Engine");
            if (tab) Body(DrawEngine);
        }
        using (var tab = ImRaii.TabItem(TabLabel("Languages"), TabFlags("Languages")))
        {
            plugin.Tutorial.Anchor("config.tab.Languages");
            if (tab) Body(DrawLanguages);
        }
        using (var tab = ImRaii.TabItem(TabLabel("Chat"), TabFlags("Chat")))
        {
            plugin.Tutorial.Anchor("config.tab.Chat");
            if (tab) Body(DrawChat);
        }
        using (var tab = ImRaii.TabItem(TabLabel("Roleplay"), TabFlags("Roleplay")))
        {
            plugin.Tutorial.Anchor("config.tab.Roleplay");
            if (tab) Body(DrawRoleplay);
        }
        using (var tab = ImRaii.TabItem(TabLabel("AI"), TabFlags("AI")))
        {
            plugin.Tutorial.Anchor("config.tab.AI");
            if (tab) Body(DrawAi);
        }
        using (var tab = ImRaii.TabItem(TabLabel("Glossary"), TabFlags("Glossary")))
        {
            plugin.Tutorial.Anchor("config.tab.Glossary");
            if (tab) Body(DrawGlossary);
        }
        using (var tab = ImRaii.TabItem(TabLabel("Help"), TabFlags("Help")))
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

    /// <summary>Translated tab title with a stable ImGui id (the tour and the saved tab use the English name).</summary>
    private static string TabLabel(string id) => $"{L(id)}###{id}";

    /// <summary>Lets the guided tour switch to a tab.</summary>
    private ImGuiTabItemFlags TabFlags(string label) =>
        plugin.Tutorial.ConsumeTabRequest(label) ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;

    private void DrawEngine()
    {
        var cfg = plugin.Configuration;
        var changed = false;

        Ui.Section(L("Translation engine"), FontAwesomeIcon.Language, first: true);
        var engine = (int)cfg.Engine;
        string[] engines = { L("DeepL (best quality, free API key)"), L("Google Translate (no key needed)") };
        ImGui.SetNextItemWidth(-1);
        if (ImGui.Combo("##engine", ref engine, engines, engines.Length))
        {
            cfg.Engine = (TranslationEngine)engine;
            plugin.Translator.ClearCache();
            changed = true;
        }

        if (cfg.Engine == TranslationEngine.DeepL)
        {
            Ui.Section(L("DeepL API keys"), FontAwesomeIcon.Key,
                   help: L("Keys are used in order. When one runs out of its monthly quota (or is invalid), the " +
                                       "next one is used automatically for the same message. Google is only used when " +
                                       "all keys fail (if the fallback below is on)."));

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
                            if (Ui.GhostIconButton("up", FontAwesomeIcon.ArrowUp, L("Use this key earlier")))
                                moveUp = i;
                        }
                        ImGui.SameLine();
                        if (Ui.GhostIconButton("rm", FontAwesomeIcon.TrashAlt, L("Remove this key"), Ui.Danger))
                            remove = i;
                    }
                }
            }
            if (keys.Count == 0)
                Ui.Status(L("No key yet: translations use Google until you add one."), StatusKind.Warning);
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

            ImGui.SetNextItemWidth(-(Ui.IconTextButtonWidth(FontAwesomeIcon.Plus, L("Add key")) + ImGui.GetFrameHeight() +
                                     ImGui.CalcTextSize(L("Show")).X + ImGui.GetStyle().ItemSpacing.X * 3 + ImGui.GetStyle().ItemInnerSpacing.X));
            ImGui.InputTextWithHint("##newkey", L("Paste a DeepL API key (ends in :fx for Free)"), ref newKey, 128,
                                    showKey ? ImGuiInputTextFlags.None : ImGuiInputTextFlags.Password);
            ImGui.SameLine();
            using (ImRaii.Disabled(string.IsNullOrWhiteSpace(newKey)))
            {
                if (Ui.Button(FontAwesomeIcon.Plus, L("Add key"), primary: true))
                {
                    var k = newKey.Trim();
                    if (!keys.Contains(k))
                        keys.Add(k);
                    newKey = string.Empty;
                    deepl.ResetKeyStates();
                    changed = true;
                    _ = deepl.GetUsageAsync(k, default).ContinueWith(_ => { }); // show status/usage right away
                }
            }
            ImGui.SameLine();
            ImGui.Checkbox(L("Show"), ref showKey);

            using (ImRaii.Disabled(checkingUsage || keys.Count == 0))
            {
                if (Ui.Button(FontAwesomeIcon.SyncAlt, L("Check usage")))
                {
                    checkingUsage = true;
                    _ = deepl.RefreshAllUsageAsync(default).ContinueWith(_ => checkingUsage = false);
                }
            }
            ImGui.SameLine();
            if (Ui.Button(FontAwesomeIcon.Redo, L("Retry used-up keys"),
                          tooltip: L("Keys that ran out are skipped for a few hours. Use this after your monthly quota resets.")))
                deepl.ResetKeyStates();

            Ui.Hint(L("Free plan: 500,000 characters/month per account (deepl.com > API > \"DeepL API Free\"). " +
                              "Keys are stored in plain text in your Dalamud plugin config folder."));

            Ui.Section(L("DeepL options"), FontAwesomeIcon.Cog);
            var f = Array.FindIndex(Formalities, x => x.Value == cfg.DeepLFormality);
            if (f < 0) f = 0;
            ImGui.SetNextItemWidth(Ui.S(200));
            if (ImGui.Combo(L("Formality (FR/DE/ES/IT/PT/NL/JA/RU)"), ref f, Formalities.Select(x => L(x.Label)).ToArray(), Formalities.Length))
            {
                cfg.DeepLFormality = Formalities[f].Value;
                plugin.Translator.ClearCache();
                changed = true;
            }

            var gb = cfg.EnglishVariant == "EN-GB";
            if (ImGui.Checkbox(L("British English output (instead of American)"), ref gb))
            {
                cfg.EnglishVariant = gb ? "EN-GB" : "EN-US";
                plugin.Translator.ClearCache();
                changed = true;
            }
        }
        if (cfg.Engine == TranslationEngine.DeepL)
        {
            var fb = cfg.FallbackToGoogle;
            if (ImGui.Checkbox(L("Use Google automatically if DeepL fails"), ref fb))
            {
                cfg.FallbackToGoogle = fb;
                changed = true;
            }
            ImGuiComponents.HelpMarker(L("Quota used up, network error or wrong key: the message is translated with Google " +
                                       "instead of being lost. You get one warning in chat every 5 minutes at most."));
        }
        else
        {
            Ui.Hint(L("Google's free web endpoint works without setup, but it is unofficial, can rate-limit " +
                              "heavy use, and is a bit less natural for English/French than DeepL."));
        }

        if (plugin.Translator.GoogleInUse)
            DrawGoogleStatus();

        ImGui.Dummy(Ui.S(0, 8));
        Ui.MutedText(LF("Sent this session: {0:N0} requests, {1:N0} characters",
                        plugin.Translator.RequestsSent, plugin.Translator.CharactersSent));

        if (changed)
            plugin.SaveConfig();
    }

    /// <summary>Google's pause state, with a way to try again right away.</summary>
    private void DrawGoogleStatus()
    {
        Ui.Section(L("Google"), FontAwesomeIcon.Globe,
                   help: L("To avoid being blocked, LinguaBridge sends Google at most a few requests per minute. If Google " +
                           "still answers \"too many requests\", translation pauses (2, 5, 10, 20, then 30 minutes), then " +
                           "LinguaBridge checks by itself whether Google answers again and carries on. Meanwhile messages " +
                           "are shown untranslated, or translated by DeepL / AI if you set one up."));
        if (plugin.Translator.Google.PauseRemaining is { } left)
        {
            Ui.Status(LF("Paused (too many requests): resumes by itself at {0:HH:mm}.", DateTime.Now + left), StatusKind.Warning);
            if (Ui.Button(FontAwesomeIcon.Redo, L("Retry now"), tooltip: L("Checks right away whether Google answers again.")))
                plugin.Translator.Google.ResumeNow();
        }
        else
        {
            Ui.Status(L("Available."), StatusKind.Success);
        }
    }

    private void DrawLanguages()
    {
        var cfg = plugin.Configuration;
        var names = Languages.All.Select(l => $"{L(l.Name)} ({l.Code})").ToArray();
        var changed = false;

        Ui.Section(L("Plugin language"), FontAwesomeIcon.Language, first: true,
                   help: L("Language of these menus, tips and messages. It does not change the languages your chat is translated between."));
        var ui = Localization.Loc.Current;
        if (SetupWizard.LanguageButtons(ref ui))
            plugin.SetUiLanguage(ui);

        Ui.Section(L("Language pair"), FontAwesomeIcon.ExchangeAlt);
        var a = IndexOf(cfg.LanguageA);
        ImGui.SetNextItemWidth(Ui.FieldWidth);
        if (ImGui.Combo(L("Language A"), ref a, names, names.Length))
        {
            // Picking the other language swaps the two (A and B can never be the same).
            if (Languages.All[a].Code == cfg.LanguageB)
                cfg.LanguageB = cfg.LanguageA;
            cfg.LanguageA = Languages.All[a].Code;
            changed = true;
        }

        var b = IndexOf(cfg.LanguageB);
        ImGui.SetNextItemWidth(Ui.FieldWidth);
        if (ImGui.Combo(L("Language B"), ref b, names, names.Length))
        {
            if (Languages.All[b].Code == cfg.LanguageA)
                cfg.LanguageA = cfg.LanguageB;
            cfg.LanguageB = Languages.All[b].Code;
            changed = true;
        }
        Ui.Hint(L("Your messages sent with /tr go from A to B or from B to A automatically."));

        Ui.Section(L("Reading"), FontAwesomeIcon.Book);
        var p = IndexOf(cfg.PreferredLanguage);
        ImGui.SetNextItemWidth(Ui.FieldWidth);
        if (ImGui.Combo(L("Language you read best"), ref p, names, names.Length))
        {
            cfg.PreferredLanguage = Languages.All[p].Code;
            changed = true;
        }
        ImGuiComponents.HelpMarker(L("Incoming chat is translated into this language, plus Party Finder descriptions."));

        var oneWay = cfg.IncomingOnlyToPreferred;
        if (ImGui.Checkbox(L("Only translate incoming chat into the language I read best"), ref oneWay))
        {
            cfg.IncomingOnlyToPreferred = oneWay;
            changed = true;
        }
        ImGuiComponents.HelpMarker(L("On: messages already in your language are left alone (e.g. reading English, only French " +
                                   "gets translated).\nOff: both directions (English is also translated to French). " +
                                   "/tr always works both ways."));

        var expand = cfg.ExpandAbbreviations;
        if (ImGui.Checkbox(L("Expand chat abbreviations before translating (jsp, tkt, idk, tbh...)"), ref expand))
        {
            cfg.ExpandAbbreviations = expand;
            changed = true;
        }

        DrawLanguageFilter(ref changed);

        Ui.Section(L("Never translate these words"), FontAwesomeIcon.ShieldAlt);
        Ui.MutedText(L("Comma separated"));
        ImGui.SameLine();
        ImGuiComponents.HelpMarker(L("Character names, nicknames, FC / static names, inside jokes... DeepL keeps them exactly " +
                                   "as written. Item links and auto-translate phrases are always kept automatically."));
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputTextWithHint("##protected", L("e.g. Rukenu, Les Chocobos, Moogle"), ref protectedText, 2000))
            cfg.ProtectedTerms = SplitList(protectedText);
        changed |= Committed();

        Ui.Section(L("Learning a language"), FontAwesomeIcon.GraduationCap,
                   help: L("Adds a second line under each translated message, in the language you are learning.\n" +
                                   "Example: you read Spanish, a German friend writes in German. You get the Spanish " +
                                   "translation as usual, plus the same message in English, to pick it up little by little.\n" +
                                   "Each learning line is one more request to the engine (DeepL characters)."));
        var learningNames = new[] { L("Off") }.Concat(names).ToArray();
        var l = string.IsNullOrWhiteSpace(cfg.LearningLanguage) || Languages.TryGet(cfg.LearningLanguage) == null
            ? 0
            : IndexOf(cfg.LearningLanguage) + 1;
        ImGui.SetNextItemWidth(Ui.FieldWidth);
        if (ImGui.Combo(L("Also show messages in"), ref l, learningNames, learningNames.Length))
        {
            cfg.LearningLanguage = l == 0 ? string.Empty : Languages.All[l - 1].Code;
            changed = true;
        }

        if (!string.IsNullOrWhiteSpace(cfg.LearningLanguage))
        {
            using (ImRaii.PushIndent())
            {
                if (cfg.LearningLanguage == cfg.PreferredLanguage)
                    Ui.Status(L("This is the language you read best: messages are already translated into it."), StatusKind.Warning);

                ImGui.TextUnformatted(L("Only for these players (comma separated, empty = everyone):"));
                ImGui.SetNextItemWidth(-1);
                if (ImGui.InputTextWithHint("##learningplayers", L("e.g. Hanna Weber"), ref learningPlayersText, 1000))
                    cfg.LearningPlayers = SplitList(learningPlayersText);
                changed |= Committed();

                var untranslated = cfg.LearningForUntranslated;
                if (ImGui.Checkbox(L("Also for messages already in the language I read best"), ref untranslated))
                {
                    cfg.LearningForUntranslated = untranslated;
                    changed = true;
                }
                ImGuiComponents.HelpMarker(L("Off: only messages that get translated also get the learning line.\n" +
                                           "On: messages written in your own language get it too (more engine requests)."));

                var lcolor = cfg.LearningTagColor;
                if (ColorPicker("learningcolor", L("Learning tag colour"), ref lcolor))
                {
                    cfg.LearningTagColor = lcolor;
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

    /// <summary>Which languages of incoming chat are translated: all except the ones you understand, or only a few.</summary>
    private void DrawLanguageFilter(ref bool changed)
    {
        var cfg = plugin.Configuration;
        Ui.Section(L("Languages to translate"), FontAwesomeIcon.Filter,
                   help: L("Messages in a language you understand are left as they are: they cost no request and do not fill " +
                           "your chat. LinguaBridge recognises most messages by itself, without asking the engine."));

        var mode = (int)cfg.LanguageMode;
        if (ImGui.RadioButton(L("Every language, except the ones I understand"), ref mode, 0))
        {
            cfg.LanguageMode = IncomingLanguageMode.AllExcept;
            changed = true;
        }
        if (cfg.LanguageMode == IncomingLanguageMode.AllExcept)
            LanguageChecklist("understood", cfg.UnderstoodLanguages, alwaysOn: cfg.IncomingOnlyToPreferred ? cfg.PreferredLanguage : null,
                              alwaysOnHint: L("The language you read best is never translated."), ref changed);

        if (ImGui.RadioButton(L("Only these languages"), ref mode, 1))
        {
            cfg.LanguageMode = IncomingLanguageMode.Only;
            if (cfg.OnlyLanguages.Count == 0)
                cfg.OnlyLanguages = new[] { cfg.LanguageA, cfg.LanguageB }.Where(c => c != cfg.PreferredLanguage).Distinct().ToList();
            changed = true;
        }
        if (cfg.LanguageMode == IncomingLanguageMode.Only)
        {
            LanguageChecklist("only", cfg.OnlyLanguages, alwaysOn: null, alwaysOnHint: null, ref changed);
            if (cfg.OnlyLanguages.Count == 0)
                Ui.Status(L("No language ticked: nothing will be translated."), StatusKind.Warning);
        }

        ImGui.Dummy(Ui.S(0, 4));
        ImGui.TextUnformatted(L("Never translate these players (comma separated):"));
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputTextWithHint("##neverplayers", L("e.g. Hanna Weber, Rukenu"), ref neverPlayersText, 1000))
            cfg.NeverTranslatePlayers = SplitList(neverPlayersText);
        changed |= Committed();
        ImGui.TextUnformatted(L("Always translate these players, whatever their language (comma separated):"));
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputTextWithHint("##alwaysplayers", L("e.g. Hanna Weber"), ref alwaysPlayersText, 1000))
            cfg.AlwaysTranslatePlayers = SplitList(alwaysPlayersText);
        changed |= Committed();
        Ui.Hint(L("A first name alone (Hanna) matches that player; part of a name (Han) does not."));
    }

    /// <summary>Three columns of language checkboxes bound to <paramref name="list"/>.</summary>
    private static void LanguageChecklist(string id, List<string> list, string? alwaysOn, string? alwaysOnHint, ref bool changed)
    {
        using var indent = ImRaii.PushIndent();
        using var table = ImRaii.Table("##" + id, 3, ImGuiTableFlags.SizingStretchSame);
        if (!table)
            return;
        foreach (var lang in Languages.All)
        {
            ImGui.TableNextColumn();
            if (lang.Code == alwaysOn)
            {
                var on = true;
                using (ImRaii.Disabled())
                    ImGui.Checkbox($"{L(lang.Name)}##{id}{lang.Code}", ref on);
                if (alwaysOnHint != null)
                    Ui.Tooltip(alwaysOnHint);
                continue;
            }
            var ticked = list.Contains(lang.Code);
            if (ImGui.Checkbox($"{L(lang.Name)}##{id}{lang.Code}", ref ticked))
            {
                if (ticked) list.Add(lang.Code);
                else list.RemoveAll(c => c == lang.Code);
                changed = true;
            }
        }
    }

    private void DrawChat()
    {
        var cfg = plugin.Configuration;
        var changed = false;

        Ui.Section(L("Incoming chat"), FontAwesomeIcon.Comments, first: true);
        var enabled = cfg.IncomingEnabled;
        if (ImGui.Checkbox(L("Translate incoming chat"), ref enabled))
        {
            cfg.IncomingEnabled = enabled;
            changed = true;
        }

        Ui.Section(L("Channels to translate"), FontAwesomeIcon.Comments);
        using (var table = ImRaii.Table("channels", 2, ImGuiTableFlags.SizingStretchSame))
        {
            foreach (var (label, types) in table ? ChannelGroups : Array.Empty<(string, XivChatType[])>())
            {
                ImGui.TableNextColumn();
                var on = types.All(cfg.Channels.Contains);
                if (ImGui.Checkbox(L(label), ref on))
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

        Ui.Section(L("Filters"), FontAwesomeIcon.Search);
        var own = cfg.TranslateOwnMessages;
        if (ImGui.Checkbox(L("Translate my own messages too"), ref own))
        {
            cfg.TranslateOwnMessages = own;
            changed = true;
        }

        var combat = cfg.PauseInCombat;
        if (ImGui.Checkbox(L("Pause while in combat"), ref combat))
        {
            cfg.PauseInCombat = combat;
            changed = true;
        }

        var spam = cfg.FilterMacroSpam;
        if (ImGui.Checkbox(L("Ignore macro spam"), ref spam))
        {
            cfg.FilterMacroSpam = spam;
            changed = true;
        }
        ImGuiComponents.HelpMarker(L("Skips a player's lines when they arrive less than 0.65 s apart (multi-line macros), " +
                                   "saving DeepL characters."));

        ImGui.TextUnformatted(L("Only translate these players (comma separated, empty = everyone):"));
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##whitelist", ref whitelistText, 1000))
            cfg.PlayerWhitelist = SplitList(whitelistText);
        changed |= Committed();

        var max = cfg.MaxMessageLength;
        ImGui.SetNextItemWidth(Ui.S(150));
        if (ImGui.SliderInt(L("Max message length"), ref max, 20, 500))
            cfg.MaxMessageLength = max;
        changed |= Committed();
        ImGuiComponents.HelpMarker(L("Longer messages are not translated automatically: they are listed in the /lb window with a " +
                                   "\"Translate\" button. In roleplay mode the limit is set in Settings > Roleplay."));

        Ui.Section(L("Output"), FontAwesomeIcon.Eye);

        string[] displays = { L("Translation below the original"), L("Replace the original with its translation"),
                              L("Replace only in roleplay mode") };
        var display = (int)cfg.IncomingDisplay;
        ImGui.SetNextItemWidth(Ui.S(300));
        if (ImGui.Combo(L("How to show translations"), ref display, displays, displays.Length))
        {
            cfg.IncomingDisplay = (IncomingDisplay)display;
            changed = true;
        }
        ImGuiComponents.HelpMarker(L("Replace: you only see the translated message (with its [FR>EN] tag), in the same order, " +
                                   "so long RP posts are not shown twice. If a translation fails or takes over 12 s, " +
                                   "the original is shown instead. Originals stay in the /lb history window."));

        var same = cfg.PrintInSameChannel;
        if (ImGui.Checkbox(L("Show translation in the same channel as the original"), ref same))
        {
            cfg.PrintInSameChannel = same;
            changed = true;
        }
        if (!cfg.PrintInSameChannel)
        {
            var idx = Math.Max(0, Array.FindIndex(OutputChannels, c => c.Type == cfg.FallbackOutputChannel));
            ImGui.SetNextItemWidth(Ui.S(150));
            if (ImGui.Combo(L("Output channel"), ref idx, OutputChannels.Select(c => L(c.Label)).ToArray(), OutputChannels.Length))
            {
                cfg.FallbackOutputChannel = OutputChannels[idx].Type;
                changed = true;
            }
        }
        else if (cfg.IncomingDisplay != IncomingDisplay.Replace)
        {
            var tellsEcho = cfg.TellsToEcho;
            if (ImGui.Checkbox(L("Show translations of tells in Echo"), ref tellsEcho))
            {
                cfg.TellsToEcho = tellsEcho;
                changed = true;
            }
            ImGuiComponents.HelpMarker(L("Otherwise the translation shows up as a second tell from that player, and other plugins " +
                                       "(notification sounds, Discord bridges, chat plugins) treat it as a new tell."));
        }

        var color = cfg.TagColor;
        if (ColorPicker("tagcolor", L("Tag colour"), ref color))
        {
            cfg.TagColor = color;
            changed = true;
        }

        var historySize = cfg.HistorySize;
        ImGui.SetNextItemWidth(Ui.S(150));
        if (ImGui.SliderInt(L("Messages kept in the /lb history"), ref historySize, 50, 1000))
            cfg.HistorySize = historySize;
        changed |= Committed();

        var shortTag = cfg.ShortTag;
        if (ImGui.Checkbox(L("Short tag: [FR] instead of [EN>FR]"), ref shortTag))
        {
            cfg.ShortTag = shortTag;
            changed = true;
        }
        ImGuiComponents.HelpMarker(L("Takes less room, handy in narrow chat bubbles such as Aetherphone's Linkpearl."));

        var echo = cfg.EchoOriginalOnSend;
        if (ImGui.Checkbox(L("After /tr, show my original text in Echo"), ref echo))
        {
            cfg.EchoOriginalOnSend = echo;
            changed = true;
        }

        DrawAetherphone(ref changed);

        Ui.Section(L("Extras"), FontAwesomeIcon.Magic);

        var dtr = cfg.ShowDtrEntry;
        if (ImGui.Checkbox(L("Show \"LB\" on/off switch in the server info bar"), ref dtr))
        {
            cfg.ShowDtrEntry = dtr;
            changed = true;
        }
        ImGuiComponents.HelpMarker(L("Top right of the screen. Left click toggles translation, right click opens the window."));

        var pf = cfg.PartyFinderMenu;
        if (ImGui.Checkbox(L("Party Finder: \"Translate description\" in the right-click menu"), ref pf))
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

        Ui.Section(L("Roleplay mode"), FontAwesomeIcon.TheaterMasks, first: true);
        var rp = cfg.RoleplayMode;
        if (ImGui.Checkbox(L("Roleplay mode"), ref rp))
            plugin.SetRoleplayMode(rp, announce: false);
        ImGui.SameLine();
        Ui.MutedText(L("(/lb rp toggles it)"));
        Ui.Hint(L("When on:\n" +
                          " - DeepL is told it is translating in-character prose (tone, voice, mature scenes untouched).\n" +
                          " - *actions*, \"speech\" / « speech », ((OOC)) and [OOC ...] keep their markers.\n" +
                          " - Emotes (/em) are translated, using the character name as subject; /tr /em works too.\n" +
                          " - Multi-line posts are not dropped as macro spam, and longer posts are read (see Long posts below).\n" +
                          " - IC, OOC, RP, ERP... and character names stay untouched."));

        Ui.Section(L("Translation context"), FontAwesomeIcon.Book);
        var f = Array.FindIndex(Formalities, x => x.Value == cfg.RpFormality);
        if (f < 0) f = 2;
        ImGui.SetNextItemWidth(Ui.S(200));
        if (ImGui.Combo(L("Formality in RP"), ref f, Formalities.Select(x => L(x.Label)).ToArray(), Formalities.Length))
        {
            cfg.RpFormality = Formalities[f].Value;
            plugin.Translator.ClearCache();
            changed = true;
        }
        ImGuiComponents.HelpMarker(L("\"Engine default\" lets DeepL pick tu/vous from the conversation context, which is " +
                                   "usually right in RP. Force \"vous\" for courtly / noble characters."));

        var lines = cfg.ContextLines;
        ImGui.SetNextItemWidth(Ui.S(200));
        if (ImGui.SliderInt(L("Conversation lines used as context"), ref lines, 0, 10))
            cfg.ContextLines = lines;
        changed |= Committed();
        ImGuiComponents.HelpMarker(L("The previous lines of the same chat (party, a tell, say + emotes...) are sent to DeepL " +
                                   "as context. DeepL does not bill context characters. It fixes pronouns, gender " +
                                   "agreement (il/elle, beau/belle), tu/vous and ambiguous words. Applies outside RP too."));

        var names = cfg.AutoProtectNames;
        if (ImGui.Checkbox(L("Never translate character names (sender and you)"), ref names))
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
        ImGui.TextUnformatted(L("Scene notes for DeepL (optional, not billed)"));
        ImGui.SameLine();
        ImGuiComponents.HelpMarker(L("Short facts that help the translation, in any language. Examples:\n" +
                                   "\"Setting: a quiet inn in Ul'dah at night. The characters are a couple, intimate and teasing. " +
                                   "Keep pet names (bebé, mon chéri, princess) as written.\""));
        if (ImGui.InputTextMultiline("##scene", ref sceneNotes, 600, new Vector2(-1, ImGui.GetTextLineHeight() * 4)))
            cfg.RpSceneNotes = sceneNotes;
        changed |= Committed();

        DrawCharacters(ref changed);

        Ui.Section(L("Long posts"), FontAwesomeIcon.PaperPlane);
        var rpMax = cfg.RoleplayMaxMessageLength;
        ImGui.SetNextItemWidth(Ui.S(200));
        if (ImGui.SliderInt(L("Longest post translated automatically in RP"), ref rpMax, 100, 1500))
            cfg.RoleplayMaxMessageLength = rpMax;
        changed |= Committed();
        ImGuiComponents.HelpMarker(L("Longer posts are listed in the /lb window with a \"Translate\" button."));

        var split = cfg.SplitLongMessages;
        if (ImGui.Checkbox(L("Split translations longer than the chat limit into several lines"), ref split))
        {
            cfg.SplitLongMessages = split;
            changed = true;
        }
        if (cfg.SplitLongMessages)
        {
            var markers = cfg.SplitMarkers;
            if (ImGui.Checkbox(L("Add (1/2), (2/2) markers"), ref markers))
            {
                cfg.SplitMarkers = markers;
                changed = true;
            }
        }

        ImGui.Dummy(Ui.S(0, 10));
        Ui.Callout(FontAwesomeIcon.ShieldAlt, Ui.Warning, L("Privacy"), L("Translated text is sent to DeepL or Google. According to DeepL, texts sent with the free API " +
                          "may be kept and used to improve their service, while DeepL Pro deletes them after translation. " +
                          "Google's free endpoint gives no guarantees. Keep that in mind for private or intimate scenes " +
                          "(you can pause translation with the LB switch or /lb off at any time)."));

        if (changed)
            plugin.SaveConfig();
    }

    private void DrawCharacters(ref bool changed)
    {
        var cfg = plugin.Configuration;
        Ui.Section(L("Characters"), FontAwesomeIcon.Book,
                   help: L("Gender and short notes per character. Sent to DeepL (not billed) only when that " +
                                   "character is in the conversation. Fixes il/elle, sur/sure, beau/belle, he/she, " +
                                   "and helps with tone (\"they are a couple, very affectionate\"). Works outside RP too."));

        var me = Plugin.PlayerState.CharacterName;
        var quick = false;
        if (!string.IsNullOrEmpty(me) && !cfg.Characters.Any(c => c.Name.Equals(me, StringComparison.OrdinalIgnoreCase)))
        {
            quick = true;
            if (Ui.Button(FontAwesomeIcon.Plus, LF("Add me ({0})", me)))
            {
                cfg.Characters.Add(new CharacterProfile { Name = me });
                changed = true;
            }
        }
        var last = plugin.History.FirstOrDefault(h => h.FromPlayer)?.Sender;
        if (last != null && !cfg.Characters.Any(c => c.Name.Equals(last, StringComparison.OrdinalIgnoreCase)))
        {
            if (quick)
                ImGui.SameLine();
            if (Ui.Button(FontAwesomeIcon.Plus, LF("Add {0}", last), tooltip: L("The last person who wrote to you")))
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
                ImGui.TableSetupColumn(L("Name"), ImGuiTableColumnFlags.WidthFixed, Ui.S(140));
                ImGui.TableSetupColumn(L("Gender"), ImGuiTableColumnFlags.WidthFixed, Ui.S(105));
                ImGui.TableSetupColumn(L("Notes"), ImGuiTableColumnFlags.WidthStretch);
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
                        c.Name = name;
                    changed |= Committed();
                    ImGui.TableNextColumn();
                    var g = Math.Max(0, Array.IndexOf(CharacterProfile.Genders, c.Gender));
                    ImGui.SetNextItemWidth(-1);
                    if (ImGui.Combo("##g", ref g, Array.ConvertAll(CharacterProfile.Genders, L), CharacterProfile.Genders.Length))
                    {
                        c.Gender = CharacterProfile.Genders[g];
                        changed = true;
                    }
                    ImGui.TableNextColumn();
                    var notes = c.Notes;
                    ImGui.SetNextItemWidth(-1);
                    if (ImGui.InputTextWithHint("##o", L("e.g. her boyfriend, affectionate, calls her 'princess'"), ref notes, 200))
                        c.Notes = notes;
                    changed |= Committed();
                    ImGui.TableNextColumn();
                    if (Ui.GhostIconButton("rm", FontAwesomeIcon.TrashAlt, LF("Remove {0}", c.Name), Ui.Danger))
                        remove = i;
                }
            }
        }
        if (Ui.Button(FontAwesomeIcon.Plus, L("Add character")))
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

        Ui.Section(L("AI translation"), FontAwesomeIcon.Robot, first: true);
        Ui.Hint(L("Use AI models (any OpenAI-compatible API) as translator. They understand idioms, tone and " +
                          "whole scenes much better than DeepL; best for roleplay. Providers are tried in order; " +
                          "if all fail, DeepL / Google are used."));

        string[] usages = { L("Off"), L("Only in roleplay mode (recommended)"), L("Always") };
        var usage = (int)cfg.LlmUsage;
        ImGui.SetNextItemWidth(Ui.S(260));
        if (ImGui.Combo(L("When to use AI"), ref usage, usages, usages.Length))
        {
            cfg.LlmUsage = (LlmUsage)usage;
            plugin.Translator.ClearCache();
            changed = true;
        }
        Ui.Status(plugin.Translator.UsingLlm ? L("AI is active right now.") :
                  llm.ActiveProviders.Count == 0 ? L("No provider configured yet.") : L("AI is not active in the current mode."),
                  plugin.Translator.UsingLlm ? StatusKind.Success : llm.ActiveProviders.Count == 0 ? StatusKind.Warning : StatusKind.Info);

        Ui.Section(L("Providers (tried top to bottom)"), FontAwesomeIcon.Robot);

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
                if (ImGui.InputText(L("Name"), ref name, 40)) p.Name = name;
                changed |= Committed();
                var url = p.BaseUrl;
                ImGui.SetNextItemWidth(Ui.S(-90));
                if (ImGui.InputText(L("Base URL"), ref url, 200)) p.BaseUrl = url.Trim();
                changed |= Committed();
                var key = p.ApiKey;
                ImGui.SetNextItemWidth(Ui.S(-90));
                if (ImGui.InputText(L("API key"), ref key, 200, showKey ? ImGuiInputTextFlags.None : ImGuiInputTextFlags.Password))
                    p.ApiKey = key.Trim();
                changed |= Committed();
                var model = p.Model;
                ImGui.SetNextItemWidth(Ui.S(-200));
                if (ImGui.InputText(L("Model"), ref model, 120)) p.Model = model.Trim();
                changed |= Committed();
                ImGui.SameLine();
                if (ImGui.SmallButton(L("List models")))
                {
                    aiTestResults[p.Id] = (L("Loading models..."), StatusKind.Busy);
                    _ = llm.ListModelsAsync(p, default).ContinueWith(t =>
                    {
                        if (t.IsCompletedSuccessfully)
                        {
                            aiModels[p.Id] = t.Result;
                            aiTestResults[p.Id] = (LF("{0} models found", t.Result.Count), StatusKind.Info);
                        }
                        else
                        {
                            aiTestResults[p.Id] = (L("Could not list models: ") + t.Exception?.GetBaseException().Message, StatusKind.Error);
                        }
                    });
                }
                if (aiModels.TryGetValue(p.Id, out var models) && models.Count > 0)
                {
                    var current = Math.Max(0, models.IndexOf(p.Model));
                    ImGui.SetNextItemWidth(Ui.S(-200));
                    if (ImGui.Combo(L("Pick model"), ref current, models.ToArray(), models.Count))
                    {
                        p.Model = models[current];
                        changed = true;
                    }
                }

                if (Ui.Button(FontAwesomeIcon.Magic, L("Test")))
                {
                    aiTestResults[p.Id] = (L("Testing..."), StatusKind.Busy);
                    _ = llm.TestAsync(p, default).ContinueWith(t => aiTestResults[p.Id] = t.IsCompletedSuccessfully
                        ? (t.Result.Text, t.Result.Ok ? StatusKind.Success : StatusKind.Error)
                        : (L("Failed: ") + t.Exception?.GetBaseException().Message, StatusKind.Error));
                }
                ImGui.SameLine();
                using (ImRaii.Disabled(i == 0))
                    if (Ui.Button(FontAwesomeIcon.ArrowUp, L("Move up")))
                        moveUp = i;
                ImGui.SameLine();
                if (Ui.Button(FontAwesomeIcon.TrashAlt, L("Remove")))
                    remove = i;
                if (aiTestResults.TryGetValue(p.Id, out var res))
                    Ui.Status(res.Text, res.Kind);
                var preset = LlmProvider.Presets.FirstOrDefault(x => x.Url == p.BaseUrl);
                if (preset.Name != null)
                {
                    Ui.MutedText(preset.Note);
                    if (ImGui.SmallButton(L("Copy key page link")))
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

        var presetNames = LlmProvider.Presets.Select(x => x.Name).Append(L("Custom (any OpenAI-compatible URL)")).ToArray();
        ImGui.Dummy(Ui.S(0, 4));
        ImGui.SetNextItemWidth(Ui.S(260));
        ImGui.Combo("##preset", ref presetIndex, presetNames, presetNames.Length);
        ImGui.SameLine();
        if (Ui.Button(FontAwesomeIcon.Plus, L("Add provider"), primary: true))
        {
            var p = new LlmProvider();
            if (presetIndex < LlmProvider.Presets.Length)
            {
                var x = LlmProvider.Presets[presetIndex];
                p.Name = x.Name;
                p.BaseUrl = x.Url;
                p.Model = x.Model;
                ImGui.SetClipboardText(x.KeyPage);
                aiTestResults[p.Id] = (LF("Key page copied to clipboard: {0}  - paste your key above, then Test.", x.KeyPage), StatusKind.Info);
            }
            cfg.LlmProviders.Add(p);
            changed = true;
        }
        ImGui.SameLine();
        ImGui.Checkbox(L("Show keys"), ref showKey);
        if (Ui.Button(FontAwesomeIcon.Redo, L("Retry paused providers")))
            llm.ResetStates();

        ImGui.Dummy(Ui.S(0, 8));
        if (ImGui.CollapsingHeader(L("Advanced: prompt and temperature")))
        {
            var temp = cfg.LlmTemperature;
            ImGui.SetNextItemWidth(Ui.S(150));
            if (ImGui.SliderFloat(L("Temperature"), ref temp, 0f, 1f, "%.2f"))
                cfg.LlmTemperature = temp;
            changed |= Committed();
            ImGuiComponents.HelpMarker(L("0.2-0.4 keeps translations faithful. Higher = freer, more creative wording."));
            if (!aiPromptLoaded)
            {
                aiPrompt = string.IsNullOrWhiteSpace(cfg.LlmPrompt) ? LlmTranslator.DefaultPrompt : cfg.LlmPrompt;
                aiPromptLoaded = true;
            }
            Ui.MutedText(L("{target} is replaced by the target language. Keep the [[xx]] output rule."));
            if (ImGui.InputTextMultiline("##prompt", ref aiPrompt, 4000, new Vector2(-1, ImGui.GetTextLineHeight() * 10)))
                cfg.LlmPrompt = aiPrompt.Trim() == LlmTranslator.DefaultPrompt.Trim() ? string.Empty : aiPrompt;
            changed |= Committed();
            if (ImGui.SmallButton(L("Reset prompt")))
            {
                cfg.LlmPrompt = string.Empty;
                aiPrompt = LlmTranslator.DefaultPrompt;
                changed = true;
            }
        }

        ImGui.Dummy(Ui.S(0, 10));
        Ui.Callout(FontAwesomeIcon.ShieldAlt, Ui.Warning, L("Privacy"), L("Messages (yours and your partners') are sent to the provider you pick. Free plans may store " +
                          "them or use them for training; check each provider's terms. A local model (Ollama / " +
                          "LM Studio) keeps everything on your PC. Keys are stored in plain text in your plugin config."));

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

        Ui.Section(L("Glossary"), FontAwesomeIcon.Book, first: true);
        Ui.Hint(L("Your own translations for specific words: lore terms, nicknames, pet names, RP slang, " +
                          "how intimate or emotional words should be rendered... DeepL always uses these when it " +
                          "knows the source language (they are uploaded to your DeepL account as glossaries, one per " +
                          "direction and per key, automatically), and the AI providers get them as instructions."));
        if (cfg.Engine == TranslationEngine.Google && cfg.LlmUsage == LlmUsage.Off)
            Ui.Status(L("Google does not use the glossary: it only applies with DeepL or AI (Settings > Engine / AI)."), StatusKind.Warning);
        Ui.MutedText(LF("Status: {0}", plugin.Translator.DeepL.GlossaryStatus));
        if (Ui.Button(FontAwesomeIcon.Magic, L("Import recommended terms")))
        {
            var added = 0;
            foreach (var (from, to, source, target) in PairStyle.RecommendedGlossary)
            {
                if (cfg.Glossary.Any(e => e.From == from && e.To == to && e.Source.Equals(source, StringComparison.OrdinalIgnoreCase)))
                    continue;
                cfg.Glossary.Add(new GlossaryEntry { From = from, To = to, Source = source, Target = target });
                added++;
            }
            importResult = added == 0 ? L("Already imported.") : LF("{0} terms added.", added);
            changed = true;
        }
        ImGuiComponents.HelpMarker(L("Endearments (mi amor > mon amour / mein Schatz, babe > bébé / Schatz, " +
                                   "mon cœur > sweetheart...) and official FFXIV job/term names between English and French " +
                                   "(Dark Knight <> Chevalier noir) and English and German (Dark Knight <> Dunkelritter, " +
                                   "Free Company <> Freie Gesellschaft...). " +
                                   "Remove any entry you don't like."));
        if (importResult.Length > 0)
        {
            ImGui.SameLine();
            Ui.MutedText(importResult);
        }

        Ui.Section(L("Add a term"), FontAwesomeIcon.Plus);

        ImGui.SetNextItemWidth(Ui.S(60));
        ImGui.Combo("##gfrom", ref glossaryFrom, names, names.Length);
        ImGui.SameLine();
        ImGui.TextUnformatted(">");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(Ui.S(60));
        ImGui.Combo("##gto", ref glossaryTo, names, names.Length);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(Ui.S(150));
        ImGui.InputTextWithHint("##gsrc", L("word or phrase"), ref newSource, 100);
        ImGui.SameLine();
        ImGui.TextUnformatted("=");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(Ui.S(150));
        ImGui.InputTextWithHint("##gdst", L("translation"), ref newTarget, 100);
        ImGui.SameLine();
        using (ImRaii.Disabled(string.IsNullOrWhiteSpace(newSource) || string.IsNullOrWhiteSpace(newTarget) ||
                               glossaryFrom == glossaryTo))
        {
            if (Ui.Button(FontAwesomeIcon.Plus, L("Add"), primary: true))
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
                          L("Swap the direction. Each direction is a separate glossary: add EN>FR and FR>EN entries separately.")))
            (glossaryFrom, glossaryTo) = (glossaryTo, glossaryFrom);
        if (glossaryFrom == glossaryTo)
            Ui.Status(L("Pick two different languages."), StatusKind.Warning);

        Ui.Section(L("Terms"), FontAwesomeIcon.Search);
        Ui.Icon(FontAwesomeIcon.Search, Ui.Muted);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(Ui.S(200));
        ImGui.InputTextWithHint("##gfilter", L("filter..."), ref glossaryFilter, 50);
        ImGui.SameLine();
        Ui.MutedText(LF("{0} entries", cfg.Glossary.Count));

        var remove = -1;
        using (var table = ImRaii.Table("glossary", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.BordersInnerH,
                                        new Vector2(-1, Math.Max(ImGui.GetContentRegionAvail().Y, ImGui.GetFrameHeight() * 8))))
        {
            if (table)
            {
                ImGui.TableSetupColumn(L("Dir"), ImGuiTableColumnFlags.WidthFixed, Ui.S(60));
                ImGui.TableSetupColumn(L("Source"));
                ImGui.TableSetupColumn(L("Translation"));
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
                    var target = e.Target;
                    ImGui.SetNextItemWidth(-1);
                    if (ImGui.InputText("##t", ref target, 100))
                        e.Target = target;
                    if (Committed() && e.Target.Trim().Length > 0)
                    {
                        e.Target = e.Target.Trim();
                        changed = true;
                    }
                    Ui.Tooltip(L("Click to edit the translation"));
                    ImGui.TableNextColumn();
                    if (Ui.GhostIconButton("rm", FontAwesomeIcon.TrashAlt, LF("Remove \"{0}\"", e.Source), Ui.Danger))
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
        Ui.Section(L("Aetherphone (Linkpearl)"), FontAwesomeIcon.Comments);
        if (detected)
        {
            ImGui.SameLine();
            Ui.Badge(L("detected"), Ui.Success);
        }

        var handled = cfg.TranslateHandledMessages;
        if (ImGui.Checkbox(L("Translate messages other plugins hid from the chat log"), ref handled))
        {
            cfg.TranslateHandledMessages = handled;
            changed = true;
        }
        ImGuiComponents.HelpMarker(L("Turn this on if a Linkpearl channel is set to \"hide from game chat\": otherwise " +
                                   "those messages are never translated. The translation is printed in the chat log " +
                                   "(that is how it reaches Linkpearl), the original stays hidden.\n" +
                                   "Careful: spam filters hide messages too, and their translations would show up."));

        if (!detected)
            return;
        using (ImRaii.PushColor(ImGuiCol.Text, Ui.Muted))
        {
            ImGui.TextWrapped(
                L("- Linkpearl shows the translations as a second message from the same player, also in Replace mode " +
                "(Aetherphone keeps the original). \"Below the original\" + short tag reads best there.\n" +
                "- Send translated from Linkpearl's composer with /tr and the channel: /tr /p hello, " +
                "/tr /t Name@World hello (without a channel it goes to the game's current channel, not the Linkpearl tab).\n" +
                "- ChocoChat and Velvet are not game chat: use the main window in \"Translate only\" or /lb clip."));
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
        Ui.Section(L("Getting started"), FontAwesomeIcon.Flag, first: true);
        if (Ui.Button(FontAwesomeIcon.Magic, L("Open setup wizard")))
            plugin.OpenWizard();
        DrawTutorialOptions();

        Ui.Section(L("How it works"), FontAwesomeIcon.InfoCircle);
        ImGui.TextWrapped(L("Incoming: messages in the selected channels are translated automatically and shown below the " +
                          "original with a tag like [EN>FR]. Only you see the translation."));
        ImGui.Dummy(Ui.S(0, 2));
        ImGui.TextWrapped(L("Outgoing: type /tr followed by your message. It is translated to the other language of the " +
                          "pair and sent to your current chat channel."));

        Ui.Section(L("Commands"), FontAwesomeIcon.Book);
        using (var table = ImRaii.Table("commands", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.PadOuterX))
        {
            if (table)
            {
                ImGui.TableSetupColumn(L("Command"), ImGuiTableColumnFlags.WidthFixed, Ui.S(220));
                ImGui.TableSetupColumn(L("What it does"), ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableHeadersRow();
                foreach (var (cmd, what) in Commands)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.TextColored(Ui.Accent, cmd);
                    if (ImGui.IsItemClicked())
                        ImGui.SetClipboardText(cmd);
                    Ui.Tooltip(L("Click to copy"));
                    ImGui.TableNextColumn();
                    ImGui.TextWrapped(L(what));
                }
            }
        }

        Ui.Section(L("Good to know"), FontAwesomeIcon.InfoCircle);
        Ui.Hint(L("- Party Finder: right-click a listing > \"Translate description\"."));
        Ui.Hint(L("- Translate only (nothing sent): in /lb pick \"Translate only\", or copy any text and type /lb clip. " +
                "The translation is copied, ready to paste in Aetherphone's ChocoChat or Velvet, Discord or a website."));
        Ui.Hint(L("- Aetherphone / Linkpearl: translations show up there too (as a second message). Use /tr /p ..., " +
                "/tr /t Name@World ... in its composer to send translated. See Chat > Aetherphone."));
        Ui.Hint(L("- Your friends need the plugin too to read your messages in their language, or simply use /tr so " +
                "they receive your text already translated."));
    }

    private void DrawTutorialOptions()
    {
        var cfg = plugin.Configuration;
        ImGui.Dummy(Ui.S(0, 4));
        using (ImRaii.Group())
        {
            ImGui.TextUnformatted(L("Guided tour and tips"));
            if (Ui.Button(FontAwesomeIcon.Flag, L("Tour of the main window")))
                plugin.Tutorial.StartTour(TourContent.MainTourId);
            ImGui.SameLine();
            if (Ui.Button(FontAwesomeIcon.Cog, L("Tour of the settings")))
                plugin.Tutorial.StartTour(TourContent.SettingsTourId);
            var tips = cfg.TipsEnabled;
            if (ImGui.Checkbox(L("Show tips the first time I use a feature"), ref tips))
                plugin.Tutorial.SetTipsEnabled(tips);
            ImGui.SameLine();
            if (Ui.Button(FontAwesomeIcon.Redo, L("Show all tips again"),
                          tooltip: L("Every tip and tour offer will appear again when it makes sense.")))
                plugin.Tutorial.ResetTips();
            Ui.MutedText(L("Also: /lb tour, /lb tour settings, /lb tips on|off|reset"));
            var still = cfg.ReduceMotion;
            if (ImGui.Checkbox(L("Reduce motion (no spinner or pulsing highlight)"), ref still))
            {
                cfg.ReduceMotion = still;
                plugin.SaveConfig();
            }
        }
        plugin.Tutorial.Anchor("config.help.tutorial");
    }

    private static int IndexOf(string code)
    {
        var i = Languages.All.ToList().FindIndex(l => l.Code == code);
        return i < 0 ? 0 : i;
    }

    private static List<(ushort Id, Vector4 Color)>? uiColors;

    /// <summary>The game's text colours (UIColor sheet), loaded once.</summary>
    private static List<(ushort Id, Vector4 Color)> UiColors()
    {
        if (uiColors != null)
            return uiColors;
        var list = new List<(ushort, Vector4)>();
        var seen = new HashSet<uint>();
        try
        {
            foreach (var row in Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.UIColor>())
            {
                var c = row.Dark; // RGBA, the colour used for chat text
                if ((c & 0xFF) == 0 || row.RowId > ushort.MaxValue || !seen.Add(c))
                    continue;
                list.Add(((ushort)row.RowId, ToVector(c)));
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Could not read the UIColor sheet");
        }
        return uiColors = list;
    }

    private static Vector4 ToVector(uint rgba) =>
        new(((rgba >> 24) & 0xFF) / 255f, ((rgba >> 16) & 0xFF) / 255f, ((rgba >> 8) & 0xFF) / 255f, 1f);

    /// <summary>A swatch of the current game text colour; clicking opens a palette of all of them.</summary>
    private static bool ColorPicker(string id, string label, ref ushort value)
    {
        var colors = UiColors();
        var current = value;
        var swatch = colors.FirstOrDefault(c => c.Id == current).Color;
        var changed = false;
        if (ImGui.ColorButton($"##{id}", swatch == default ? Ui.Muted : swatch, ImGuiColorEditFlags.NoTooltip, Ui.S(22, 22)))
            ImGui.OpenPopup($"##{id}popup");
        Ui.Tooltip(L("Pick a colour"));
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label);

        using var popup = ImRaii.Popup($"##{id}popup");
        if (!popup)
            return false;
        if (colors.Count == 0)
        {
            Ui.MutedText(L("The game colours could not be read."));
            return false;
        }
        var perRow = 16;
        for (var i = 0; i < colors.Count; i++)
        {
            var (cid, color) = colors[i];
            if (i % perRow != 0)
                ImGui.SameLine(0, Ui.S(2));
            using (ImRaii.PushColor(ImGuiCol.Border, Ui.Accent, cid == current))
            using (ImRaii.PushStyle(ImGuiStyleVar.FrameBorderSize, cid == current ? Ui.S(2) : 0))
            {
                if (ImGui.ColorButton($"##{id}{cid}", color, ImGuiColorEditFlags.NoTooltip, Ui.S(18, 18)))
                {
                    value = cid;
                    changed = true;
                    ImGui.CloseCurrentPopup();
                }
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(LF("Colour {0}", cid));
        }
        return changed;
    }
}
