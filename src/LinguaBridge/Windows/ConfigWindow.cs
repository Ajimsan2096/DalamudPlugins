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

        ImGui.TextWrapped("Messages written in A are translated to B and messages written in B are translated to A.");

        var p = IndexOf(cfg.PreferredLanguage);
        ImGui.SetNextItemWidth(220);
        if (ImGui.Combo("Language you read best", ref p, names, names.Length))
        {
            cfg.PreferredLanguage = Languages.All[p].Code;
            changed = true;
        }
        ImGuiComponents.HelpMarker("Used for Party Finder descriptions and, if enabled below, for messages in other languages.");

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
            "/lb config opens settings, /lb on|off|toggle switches incoming translation, /lb wizard reruns the setup.\n\n" +
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
