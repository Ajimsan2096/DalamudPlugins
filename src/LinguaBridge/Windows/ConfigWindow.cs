using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using LinguaBridge.Translation;

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
        ("Cross-world Linkshells 1-8",
            new[] { XivChatType.CrossLinkShell1 }
                .Concat(Enumerable.Range(0, 7).Select(i => (XivChatType)((int)XivChatType.CrossLinkShell2 + i))).ToArray()),
        ("Emotes (/em) - always on in roleplay mode", new[] { XivChatType.CustomEmote }),
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
    private string sceneNotes = string.Empty;
    private bool sceneNotesLoaded;
    private int glossaryFrom;
    private int glossaryTo = 1;
    private string newSource = string.Empty;
    private string newTarget = string.Empty;
    private string glossaryFilter = string.Empty;
    private bool checkingUsage;
    private string whitelistText;
    private string protectedText;

    public ConfigWindow(Plugin plugin) : base("LinguaBridge Settings###LinguaBridgeConfig")
    {
        this.plugin = plugin;
        Size = new Vector2(520, 560);
        SizeCondition = ImGuiCond.FirstUseEver;
        whitelistText = string.Join(", ", plugin.Configuration.PlayerWhitelist);
        protectedText = string.Join(", ", plugin.Configuration.ProtectedTerms);
    }

    public override void Draw()
    {
        using var tabs = ImRaii.TabBar("tabs");
        if (!tabs)
            return;
        using (var tab = ImRaii.TabItem("Engine"))
            if (tab) DrawEngine();
        using (var tab = ImRaii.TabItem("Languages"))
            if (tab) DrawLanguages();
        using (var tab = ImRaii.TabItem("Chat"))
            if (tab) DrawChat();
        using (var tab = ImRaii.TabItem("Roleplay"))
            if (tab) DrawRoleplay();
        using (var tab = ImRaii.TabItem("Glossary"))
            if (tab) DrawGlossary();
        using (var tab = ImRaii.TabItem("Help"))
            if (tab) DrawHelp();
    }

    private void DrawEngine()
    {
        var cfg = plugin.Configuration;
        var changed = false;

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
            ImGui.Spacing();
            ImGui.TextUnformatted("DeepL API keys");
            ImGuiComponents.HelpMarker("Keys are used in order. When one runs out of its monthly quota (or is invalid), the " +
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
                    ImGui.TableSetupColumn("key", ImGuiTableColumnFlags.WidthFixed, 190);
                    ImGui.TableSetupColumn("status", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableSetupColumn("actions", ImGuiTableColumnFlags.WidthFixed, 70);
                    for (var i = 0; i < keys.Count; i++)
                    {
                        using var id = ImRaii.PushId(i);
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted($"#{i + 1}  {(showKey ? keys[i] : DeepLTranslator.Mask(keys[i]))}");
                        ImGui.TableNextColumn();
                        ImGui.TextDisabled(deepl.Describe(keys[i]));
                        ImGui.TableNextColumn();
                        using (ImRaii.Disabled(i == 0))
                        {
                            if (ImGui.SmallButton("Up"))
                                moveUp = i;
                        }
                        ImGui.SameLine();
                        if (ImGui.SmallButton("X"))
                            remove = i;
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip("Remove this key");
                    }
                }
            }
            if (keys.Count == 0)
                ImGui.TextColored(new Vector4(1, 0.7f, 0.3f, 1), "No key yet: translations use Google until you add one.");
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

            ImGui.SetNextItemWidth(-150);
            ImGui.InputTextWithHint("##newkey", "Paste a DeepL API key (ends in :fx for Free)", ref newKey, 128,
                                    showKey ? ImGuiInputTextFlags.None : ImGuiInputTextFlags.Password);
            ImGui.SameLine();
            using (ImRaii.Disabled(string.IsNullOrWhiteSpace(newKey)))
            {
                if (ImGui.Button("Add key"))
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
                if (ImGui.Button("Check usage of all keys"))
                {
                    checkingUsage = true;
                    _ = deepl.RefreshAllUsageAsync(default).ContinueWith(_ => checkingUsage = false);
                }
            }
            ImGui.SameLine();
            if (ImGui.Button("Retry used-up keys"))
                deepl.ResetKeyStates();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Keys that ran out are skipped for a few hours. Use this after your monthly quota resets.");

            ImGui.TextWrapped("Free plan: 500,000 characters/month per account (deepl.com > API > \"DeepL API Free\"). " +
                              "Keys are stored in plain text in your Dalamud plugin config folder.");

            ImGui.Spacing();
            var f = Array.FindIndex(Formalities, x => x.Value == cfg.DeepLFormality);
            if (f < 0) f = 0;
            ImGui.SetNextItemWidth(200);
            if (ImGui.Combo("Formality (FR/DE/ES/IT/PT/JA)", ref f, Formalities.Select(x => x.Label).ToArray(), Formalities.Length))
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
            ImGui.TextWrapped("Google's free web endpoint works without setup, but it is unofficial, can rate-limit " +
                              "heavy use, and is a bit less natural for English/French than DeepL.");
        }

        ImGui.Spacing();
        ImGui.TextDisabled($"Characters sent this session: {plugin.Translator.CharactersSent:N0}");

        if (changed)
            plugin.SaveConfig();
    }

    private void DrawLanguages()
    {
        var cfg = plugin.Configuration;
        var names = Languages.All.Select(l => $"{l.Name} ({l.Code})").ToArray();
        var changed = false;

        var a = IndexOf(cfg.LanguageA);
        ImGui.SetNextItemWidth(220);
        if (ImGui.Combo("Language A", ref a, names, names.Length))
        {
            cfg.LanguageA = Languages.All[a].Code;
            changed = true;
        }

        var b = IndexOf(cfg.LanguageB);
        ImGui.SetNextItemWidth(220);
        if (ImGui.Combo("Language B", ref b, names, names.Length))
        {
            cfg.LanguageB = Languages.All[b].Code;
            changed = true;
        }

        if (cfg.LanguageA == cfg.LanguageB)
            ImGui.TextColored(new Vector4(1, 0.4f, 0.4f, 1), "Language A and B must be different.");

        ImGui.TextWrapped("Your messages sent with /tr go from A to B or from B to A automatically.");

        var p = IndexOf(cfg.PreferredLanguage);
        ImGui.SetNextItemWidth(220);
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

        ImGui.Spacing();
        ImGui.TextUnformatted("Never translate these words (comma separated):");
        ImGuiComponents.HelpMarker("Character names, nicknames, FC / static names, inside jokes... DeepL keeps them exactly " +
                                   "as written. Item links and auto-translate phrases are always kept automatically.");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputTextWithHint("##protected", "e.g. Rukenu, Les Chocobos, Moogle", ref protectedText, 2000))
        {
            cfg.ProtectedTerms = protectedText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            changed = true;
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

        var enabled = cfg.IncomingEnabled;
        if (ImGui.Checkbox("Translate incoming chat", ref enabled))
        {
            cfg.IncomingEnabled = enabled;
            changed = true;
        }

        ImGui.TextUnformatted("Channels to translate:");
        using (ImRaii.PushIndent())
        {
            foreach (var (label, types) in ChannelGroups)
            {
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
        ImGui.SetNextItemWidth(150);
        if (ImGui.SliderInt("Max message length", ref max, 20, 500))
        {
            cfg.MaxMessageLength = max;
            changed = true;
        }

        ImGui.Separator();
        ImGui.TextUnformatted("Output");

        var same = cfg.PrintInSameChannel;
        if (ImGui.Checkbox("Show translation in the same channel as the original", ref same))
        {
            cfg.PrintInSameChannel = same;
            changed = true;
        }
        if (!cfg.PrintInSameChannel)
        {
            var idx = Math.Max(0, Array.IndexOf(OutputChannels, cfg.FallbackOutputChannel));
            ImGui.SetNextItemWidth(150);
            if (ImGui.Combo("Output channel", ref idx, OutputChannels.Select(c => c.ToString()).ToArray(), OutputChannels.Length))
            {
                cfg.FallbackOutputChannel = OutputChannels[idx];
                changed = true;
            }
        }

        int color = cfg.TagColor;
        ImGui.SetNextItemWidth(150);
        if (ImGui.InputInt("Tag colour (UIColor id)", ref color))
        {
            cfg.TagColor = (ushort)Math.Clamp(color, 0, 1000);
            changed = true;
        }

        var echo = cfg.EchoOriginalOnSend;
        if (ImGui.Checkbox("After /tr, show my original text in Echo", ref echo))
        {
            cfg.EchoOriginalOnSend = echo;
            changed = true;
        }

        ImGui.Separator();
        ImGui.TextUnformatted("Extras");

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

        var rp = cfg.RoleplayMode;
        if (ImGui.Checkbox("Roleplay mode", ref rp))
            plugin.SetRoleplayMode(rp, announce: false);
        ImGui.SameLine();
        ImGui.TextDisabled("(/lb rp toggles it)");
        ImGui.TextWrapped("When on:\n" +
                          " - DeepL is told it is translating in-character prose (tone, voice, mature scenes untouched).\n" +
                          " - *actions*, \"speech\" / « speech », ((OOC)) and [OOC ...] keep their markers.\n" +
                          " - Emotes (/em) are translated, using the character name as subject; /tr /em works too.\n" +
                          " - Multi-line posts are not dropped as macro spam, and posts up to 600 characters are read.\n" +
                          " - IC, OOC, RP, ERP... and character names stay untouched.");

        ImGui.Separator();
        var f = Array.FindIndex(Formalities, x => x.Value == cfg.RpFormality);
        if (f < 0) f = 2;
        ImGui.SetNextItemWidth(200);
        if (ImGui.Combo("Formality in RP", ref f, Formalities.Select(x => x.Label).ToArray(), Formalities.Length))
        {
            cfg.RpFormality = Formalities[f].Value;
            plugin.Translator.ClearCache();
            changed = true;
        }
        ImGuiComponents.HelpMarker("\"Engine default\" lets DeepL pick tu/vous from the conversation context, which is " +
                                   "usually right in RP. Force \"vous\" for courtly / noble characters.");

        var lines = cfg.ContextLines;
        ImGui.SetNextItemWidth(200);
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
        ImGui.Spacing();
        ImGui.TextUnformatted("Scene notes for DeepL (optional, not billed):");
        ImGuiComponents.HelpMarker("Short facts that help the translation, in any language. Examples:\n" +
                                   "\"Setting: a quiet inn in Ul'dah at night. The characters are a couple, intimate and teasing. " +
                                   "Keep pet names (bebé, mon chéri, princess) as written.\"");
        if (ImGui.InputTextMultiline("##scene", ref sceneNotes, 600, new Vector2(-1, ImGui.GetTextLineHeight() * 4)))
        {
            cfg.RpSceneNotes = sceneNotes;
            changed = true;
        }

        ImGui.Separator();
        DrawCharacters(ref changed);

        ImGui.Separator();
        ImGui.TextUnformatted("Sending long posts");
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

        ImGui.Separator();
        ImGui.TextColored(new Vector4(1f, 0.75f, 0.35f, 1f), "Privacy");
        ImGui.TextWrapped("Translated text is sent to DeepL or Google. According to DeepL, texts sent with the free API " +
                          "may be kept and used to improve their service, while DeepL Pro deletes them after translation. " +
                          "Google's free endpoint gives no guarantees. Keep that in mind for private or intimate scenes " +
                          "(you can pause translation with the LB switch or /lb off at any time).");

        if (changed)
            plugin.SaveConfig();
    }

    private void DrawCharacters(ref bool changed)
    {
        var cfg = plugin.Configuration;
        ImGui.TextUnformatted("Characters");
        ImGuiComponents.HelpMarker("Gender and short notes per character. Sent to DeepL (not billed) only when that " +
                                   "character is in the conversation. Fixes il/elle, sur/sure, beau/belle, he/she, " +
                                   "and helps with tone (\"they are a couple, very affectionate\"). Works outside RP too.");

        var me = Plugin.PlayerState.CharacterName;
        if (!string.IsNullOrEmpty(me) && !cfg.Characters.Any(c => c.Name.Equals(me, StringComparison.OrdinalIgnoreCase)))
        {
            ImGui.SameLine();
            if (ImGui.SmallButton($"Add me ({me})"))
            {
                cfg.Characters.Add(new CharacterProfile { Name = me });
                changed = true;
            }
        }
        var last = plugin.History.FirstOrDefault(h => !h.Outgoing && h.Sender is not ("Listing" or "You"))?.Sender;
        if (last != null && !cfg.Characters.Any(c => c.Name.Equals(last, StringComparison.OrdinalIgnoreCase)))
        {
            ImGui.SameLine();
            if (ImGui.SmallButton($"Add {last}"))
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
                ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthFixed, 140);
                ImGui.TableSetupColumn("Gender", ImGuiTableColumnFlags.WidthFixed, 105);
                ImGui.TableSetupColumn("Notes", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 22);
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
                    if (ImGui.SmallButton("X"))
                        remove = i;
                }
            }
        }
        if (ImGui.SmallButton("+ Add character"))
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

    private void DrawGlossary()
    {
        var cfg = plugin.Configuration;
        var names = Languages.All.Select(l => l.Code).ToArray();
        var changed = false;

        ImGui.TextWrapped("Your own translations for specific words: lore terms, nicknames, pet names, RP slang, " +
                          "how intimate or emotional words should be rendered... DeepL always uses these when it " +
                          "knows the source language. They are uploaded to your DeepL account as glossaries " +
                          "(one per direction and per key) automatically. DeepL only.");
        ImGui.TextDisabled($"Status: {plugin.Translator.DeepL.GlossaryStatus}");
        ImGui.Spacing();

        ImGui.SetNextItemWidth(60);
        ImGui.Combo("##gfrom", ref glossaryFrom, names, names.Length);
        ImGui.SameLine();
        ImGui.TextUnformatted(">");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(60);
        ImGui.Combo("##gto", ref glossaryTo, names, names.Length);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(150);
        ImGui.InputTextWithHint("##gsrc", "word or phrase", ref newSource, 100);
        ImGui.SameLine();
        ImGui.TextUnformatted("=");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(150);
        ImGui.InputTextWithHint("##gdst", "translation", ref newTarget, 100);
        ImGui.SameLine();
        using (ImRaii.Disabled(string.IsNullOrWhiteSpace(newSource) || string.IsNullOrWhiteSpace(newTarget) ||
                               glossaryFrom == glossaryTo))
        {
            if (ImGui.Button("Add"))
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
        if (ImGui.Button("Swap"))
            (glossaryFrom, glossaryTo) = (glossaryTo, glossaryFrom);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Each direction is a separate glossary: add EN>FR and FR>EN entries separately.");

        ImGui.SetNextItemWidth(200);
        ImGui.InputTextWithHint("##gfilter", "filter...", ref glossaryFilter, 50);
        ImGui.SameLine();
        ImGui.TextDisabled($"{cfg.Glossary.Count} entries");

        var remove = -1;
        using (var table = ImRaii.Table("glossary", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.BordersInnerH,
                                        new Vector2(-1, -1)))
        {
            if (table)
            {
                ImGui.TableSetupColumn("Dir", ImGuiTableColumnFlags.WidthFixed, 60);
                ImGui.TableSetupColumn("Source");
                ImGui.TableSetupColumn("Translation");
                ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 25);
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
                    ImGui.TextUnformatted($"{e.From}>{e.To}");
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(e.Source);
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(e.Target);
                    ImGui.TableNextColumn();
                    if (ImGui.SmallButton("X"))
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

    private void DrawHelp()
    {
        if (ImGui.Button("Open setup wizard"))
            plugin.OpenWizard();
        ImGui.Spacing();
        ImGui.TextWrapped(
            "Incoming: messages in the selected channels are translated automatically and shown below the original " +
            "with a tag like [EN>FR]. Only you see the translation.\n\n" +
            "Outgoing: type /tr followed by your message. It is translated to the other language of the pair and sent " +
            "to your current chat channel.\n\n" +
            "  /tr Hello everyone!            -> current channel\n" +
            "  /tr /p Ready for the pull?     -> party\n" +
            "  /tr /t Jane Doe@Ragnarok hi!   -> tell\n" +
            "  /tr /t <t> thanks!             -> tell to your target\n" +
            "  /tr :en /fc Merci beaucoup     -> force English\n\n" +
            "/lb (or /linguabridge) opens the history window: press \"Reply\" on a message to answer it translated in " +
            "the same channel, or use the compose box with Preview to check the meaning before sending.\n" +
            "/lb config opens settings, /lb on|off|toggle switches incoming translation, /lb rp toggles roleplay mode, /lb wizard reruns the setup.\n\n" +
            "Party Finder: right-click a listing > \"Translate description\".\n\n" +
            "Tip: your friends need the plugin too if they want to read your messages in their language; or simply use /tr " +
            "so they receive your text already translated.");
    }

    private static int IndexOf(string code)
    {
        var i = Languages.All.ToList().FindIndex(l => l.Code == code);
        return i < 0 ? 0 : i;
    }
}
