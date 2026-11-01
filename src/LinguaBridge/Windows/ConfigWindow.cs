using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
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
    private string usage = string.Empty;
    private string whitelistText;

    public ConfigWindow(Plugin plugin) : base("LinguaBridge Settings###LinguaBridgeConfig")
    {
        this.plugin = plugin;
        Size = new Vector2(520, 560);
        SizeCondition = ImGuiCond.FirstUseEver;
        whitelistText = string.Join(", ", plugin.Configuration.PlayerWhitelist);
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
            ImGui.TextUnformatted("DeepL API key");
            var key = cfg.DeepLApiKey;
            ImGui.SetNextItemWidth(-90);
            if (ImGui.InputText("##key", ref key, 128, showKey ? ImGuiInputTextFlags.None : ImGuiInputTextFlags.Password))
            {
                cfg.DeepLApiKey = key.Trim();
                changed = true;
            }
            ImGui.SameLine();
            ImGui.Checkbox("Show", ref showKey);
            ImGui.TextWrapped("Free plan: 500,000 characters/month. Create an account at deepl.com > API > \"DeepL API Free\", " +
                              "then copy the key from your account page (free keys end in \":fx\"). The key is stored in plain " +
                              "text in your Dalamud plugin config folder.");

            if (ImGui.Button("Check usage"))
            {
                usage = "Checking...";
                _ = plugin.Translator.DeepL.GetUsageAsync(default).ContinueWith(t =>
                    usage = t.IsCompletedSuccessfully
                        ? $"{t.Result.Used:N0} / {t.Result.Limit:N0} characters used this month"
                        : t.Exception?.GetBaseException().Message ?? "Error");
            }
            ImGui.SameLine();
            ImGui.TextUnformatted(usage);

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

        var expand = cfg.ExpandAbbreviations;
        if (ImGui.Checkbox("Expand chat abbreviations before translating (jsp, tkt, idk, tbh...)", ref expand))
        {
            cfg.ExpandAbbreviations = expand;
            changed = true;
        }

        var others = cfg.TranslateOtherLanguages;
        if (ImGui.Checkbox("Also translate other languages (into language B)", ref others))
        {
            cfg.TranslateOtherLanguages = others;
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

        if (changed)
            plugin.SaveConfig();
    }

    private static void DrawHelp()
    {
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
            "/linguabridge opens the history window (with a compose box), /linguabridge config opens settings, " +
            "/linguabridge on|off toggles incoming translation.\n\n" +
            "Tip: your friends need the plugin too if they want to read your messages in their language; or simply use /tr " +
            "so they receive your text already translated.");
    }

    private static int IndexOf(string code)
    {
        var i = Languages.All.ToList().FindIndex(l => l.Code == code);
        return i < 0 ? 0 : i;
    }
}
