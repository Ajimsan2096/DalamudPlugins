using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using LinguaBridge.Translation;

namespace LinguaBridge.Windows;

/// <summary>Four-step first-run setup: languages, engine (with a live test), channels, cheat sheet.</summary>
public sealed class SetupWizard : Window
{
    private const int Steps = 4;

    private static readonly (string Label, XivChatType[] Types)[] ChannelPresets =
    {
        ("Party / Alliance", new[] { XivChatType.Party, XivChatType.CrossParty, XivChatType.Alliance }),
        ("Tells", new[] { XivChatType.TellIncoming }),
        ("Free Company", new[] { XivChatType.FreeCompany }),
        ("Linkshells", Enumerable.Range(0, 8).Select(i => (XivChatType)((int)XivChatType.Ls1 + i)).ToArray()),
        ("Cross-world Linkshells",
            new[] { XivChatType.CrossLinkShell1 }
                .Concat(Enumerable.Range(0, 7).Select(i => (XivChatType)((int)XivChatType.CrossLinkShell2 + i))).ToArray()),
        ("Say", new[] { XivChatType.Say }),
        ("Shout / Yell", new[] { XivChatType.Shout, XivChatType.Yell }),
    };

    private readonly Plugin plugin;
    private int step = 1;
    private string testResult = string.Empty;
    private bool testing;

    public SetupWizard(Plugin plugin) : base("LinguaBridge setup###LinguaBridgeWizard", ImGuiWindowFlags.NoCollapse)
    {
        this.plugin = plugin;
        Size = new Vector2(500, 430);
        SizeCondition = ImGuiCond.Appearing;
    }

    public override void OnOpen() => step = 1;

    public override void Draw()
    {
        ImGui.TextDisabled($"Step {step} of {Steps}");
        ImGui.Separator();

        using (var body = ImRaii.Child("wizardBody", new Vector2(0, -ImGui.GetFrameHeightWithSpacing() - 6), false))
        {
            if (body)
            {
                switch (step)
                {
                    case 1: DrawLanguages(); break;
                    case 2: DrawEngine(); break;
                    case 3: DrawChannels(); break;
                    default: DrawFinish(); break;
                }
            }
        }

        ImGui.Separator();
        if (step > 1 && ImGui.Button("Back", new Vector2(90, 0)))
            step--;
        ImGui.SameLine(ImGui.GetContentRegionAvail().X - 180 + ImGui.GetCursorPosX());
        if (step < Steps)
        {
            if (ImGui.Button("Skip setup", new Vector2(90, 0)))
                Finish();
            ImGui.SameLine();
            if (ImGui.Button("Next", new Vector2(90, 0)))
                step++;
        }
        else
        {
            ImGui.Dummy(new Vector2(90, 0));
            ImGui.SameLine();
            if (ImGui.Button("Finish", new Vector2(90, 0)))
                Finish();
        }
    }

    private void Finish()
    {
        plugin.Configuration.ShowedWizard = true;
        plugin.Translator.ClearCache();
        plugin.SaveConfig();
        IsOpen = false;
    }

    private void DrawLanguages()
    {
        var cfg = plugin.Configuration;
        ImGui.TextWrapped("Welcome! LinguaBridge translates chat between two languages, in both directions, " +
                          "and lets you send your own messages already translated.");
        ImGui.Spacing();
        ImGui.TextUnformatted("Which two languages are spoken in your group?");

        var names = Languages.All.Select(l => l.Name).ToArray();
        var a = IndexOf(cfg.LanguageA);
        var b = IndexOf(cfg.LanguageB);
        ImGui.SetNextItemWidth(170);
        if (ImGui.Combo("##wa", ref a, names, names.Length))
            cfg.LanguageA = Languages.All[a].Code;
        ImGui.SameLine();
        ImGui.TextUnformatted("<->");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(170);
        if (ImGui.Combo("##wb", ref b, names, names.Length))
            cfg.LanguageB = Languages.All[b].Code;
        if (cfg.LanguageA == cfg.LanguageB)
            ImGui.TextColored(new Vector4(1, 0.4f, 0.4f, 1), "Pick two different languages.");

        ImGui.Spacing();
        ImGui.TextUnformatted("Which language do you read best?");
        ImGui.TextDisabled("Used for Party Finder descriptions and for messages in other languages.");
        var p = IndexOf(cfg.PreferredLanguage);
        ImGui.SetNextItemWidth(170);
        if (ImGui.Combo("##wp", ref p, names, names.Length))
            cfg.PreferredLanguage = Languages.All[p].Code;
    }

    private void DrawEngine()
    {
        var cfg = plugin.Configuration;
        ImGui.TextUnformatted("Translation engine");
        ImGui.Spacing();

        if (ImGui.RadioButton("DeepL (recommended, best quality)", cfg.Engine == TranslationEngine.DeepL))
            cfg.Engine = TranslationEngine.DeepL;
        ImGui.TextDisabled("    Free API key: 500,000 characters per month.");
        if (ImGui.RadioButton("Google Translate (no setup)", cfg.Engine == TranslationEngine.Google))
            cfg.Engine = TranslationEngine.Google;
        ImGui.TextDisabled("    Works instantly, less natural with slang and game terms.");

        if (cfg.Engine == TranslationEngine.DeepL)
        {
            ImGui.Spacing();
            ImGui.TextWrapped("Get a key: deepl.com > Pricing > API > \"DeepL API Free\" (asks for a card for verification, " +
                              "no charge). Then Account > \"API Keys & Limits\". Free keys end in \":fx\".");
            if (ImGui.Button("Copy deepl.com/pro-api to clipboard"))
                ImGui.SetClipboardText("https://www.deepl.com/pro-api");
            var key = cfg.DeepLApiKeys.Count > 0 ? cfg.DeepLApiKeys[0] : string.Empty;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputTextWithHint("##wkey", "Paste your DeepL API key here", ref key, 128, ImGuiInputTextFlags.Password))
            {
                if (cfg.DeepLApiKeys.Count == 0)
                    cfg.DeepLApiKeys.Add(key.Trim());
                else
                    cfg.DeepLApiKeys[0] = key.Trim();
                plugin.Translator.DeepL.ResetKeyStates();
            }
            ImGui.TextDisabled("You can add more keys later in /lb config > Engine.");
            var fb = cfg.FallbackToGoogle;
            if (ImGui.Checkbox("Use Google automatically if DeepL fails or runs out", ref fb))
                cfg.FallbackToGoogle = fb;
        }

        ImGui.Spacing();
        using (ImRaii.Disabled(testing))
        {
            if (ImGui.Button("Test translation"))
                _ = Test();
        }
        ImGui.SameLine();
        ImGui.TextWrapped(testResult);
    }

    private async Task Test()
    {
        testing = true;
        testResult = "Testing...";
        try
        {
            plugin.Translator.ClearCache();
            var cfg = plugin.Configuration;
            var a = Languages.Get(cfg.LanguageA);
            var b = Languages.Get(cfg.LanguageB);
            var r = await plugin.Translator.TranslateAsync("Thanks for the help, see you tonight!", b.Code == "EN" ? a : b);
            testResult = r == null ? "OK (text was already in that language)." : $"OK via {r.Engine}: {r.Text}";
        }
        catch (Exception ex)
        {
            testResult = "Failed: " + ex.Message;
        }
        finally
        {
            testing = false;
        }
    }

    private void DrawChannels()
    {
        var cfg = plugin.Configuration;
        ImGui.TextUnformatted("Which chats should be translated automatically?");
        ImGui.Spacing();
        foreach (var (label, types) in ChannelPresets)
        {
            var on = types.All(cfg.Channels.Contains);
            if (ImGui.Checkbox(label, ref on))
                foreach (var t in types)
                {
                    if (on) cfg.Channels.Add(t);
                    else cfg.Channels.Remove(t);
                }
        }

        ImGui.Spacing();
        ImGui.Separator();
        var same = cfg.PrintInSameChannel;
        if (ImGui.Checkbox("Show the translation in the same chat tab as the original", ref same))
            cfg.PrintInSameChannel = same;
        ImGui.TextDisabled("Otherwise translations go to the Echo channel.");
        var combat = cfg.PauseInCombat;
        if (ImGui.Checkbox("Pause translation while in combat", ref combat))
            cfg.PauseInCombat = combat;
    }

    private void DrawFinish()
    {
        ImGui.TextUnformatted("All set! Quick reference:");
        ImGui.Spacing();
        ImGui.TextWrapped(
            "- Messages from your friends get a translated line with a tag like [EN>FR].\n\n" +
            "- Reply translated:  /tr your message   (current channel)\n" +
            "      /tr /p ...   /tr /fc ...   /tr /t Name@World ...   /tr :en ... forces a language\n\n" +
            "- /lb opens the window: history, \"Reply\" buttons, and a box to preview translations before sending.\n\n" +
            "- The \"LB\" entry at the top right of the screen turns translation on/off (left click) " +
            "or opens the window (right click).\n\n" +
            "- In Party Finder, right-click a listing > \"Translate description\".\n\n" +
            "- /lb config for all settings, /lb wizard to see this again.");
    }

    private static int IndexOf(string code)
    {
        var i = Languages.All.ToList().FindIndex(l => l.Code == code);
        return i < 0 ? 0 : i;
    }
}
