using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using LinguaBridge.Translation;
using LinguaBridge.Tutorial;

namespace LinguaBridge.Windows;

/// <summary>Four-step first-run setup: languages, engine (with a live test), channels, cheat sheet.</summary>
public sealed class SetupWizard : Window
{
    private const int Steps = 4;
    private static readonly string[] StepNames = { "Languages", "Engine", "Channels", "Done" };

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
    private StatusKind testKind;
    private bool testing;

    public SetupWizard(Plugin plugin) : base("LinguaBridge setup###LinguaBridgeWizard", ImGuiWindowFlags.NoCollapse)
    {
        this.plugin = plugin;
        Size = new Vector2(540, 500);
        SizeCondition = ImGuiCond.Appearing;
    }

    public override void OnOpen() => step = 1;

    public override void Draw()
    {
        DrawStepper();

        var footer = ImGui.GetFrameHeightWithSpacing() + Ui.S(12);
        using (var body = ImRaii.Child("wizardBody", new Vector2(0, -footer), false))
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

        DrawFooter();
    }

    /// <summary>Numbered dots joined by a line: where you are and what is left.</summary>
    private void DrawStepper()
    {
        var dl = ImGui.GetWindowDrawList();
        var start = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var r = Ui.S(11);
        var slot = width / Steps;
        var lineY = start.Y + r;
        var muted = ImGui.GetColorU32(Ui.Muted with { W = 0.35f });
        dl.AddLine(new Vector2(start.X + slot / 2, lineY), new Vector2(start.X + width - slot / 2, lineY), muted, Ui.S(2));
        if (step > 1)
            dl.AddLine(new Vector2(start.X + slot / 2, lineY), new Vector2(start.X + slot * (step - 0.5f), lineY),
                       ImGui.GetColorU32(Ui.Accent), Ui.S(2));

        var bg = ImGui.GetColorU32(ImGuiCol.WindowBg);
        for (var i = 1; i <= Steps; i++)
        {
            var c = new Vector2(start.X + slot * (i - 0.5f), lineY);
            var done = i < step;
            var current = i == step;
            dl.AddCircleFilled(c, r, bg);
            if (done || current)
                dl.AddCircleFilled(c, r, ImGui.GetColorU32(Ui.Accent with { W = current ? 1f : 0.55f }));
            else
                dl.AddCircle(c, r, muted, 0, Ui.S(2));
            var num = done ? "+" : i.ToString();
            var ns = ImGui.CalcTextSize(num);
            dl.AddText(c - ns / 2, ImGui.GetColorU32(done || current ? new Vector4(0.05f, 0.08f, 0.12f, 1) : Ui.Muted), num);
            var label = StepNames[i - 1];
            var ls = ImGui.CalcTextSize(label);
            dl.AddText(new Vector2(c.X - ls.X / 2, lineY + r + Ui.S(4)),
                       ImGui.GetColorU32(current ? Ui.Accent : Ui.Muted), label);
        }
        ImGui.Dummy(new Vector2(width, r * 2 + Ui.S(6) + ImGui.GetTextLineHeight()));
        ImGui.Dummy(Ui.S(0, 4));
        ImGui.Separator();
        ImGui.Dummy(Ui.S(0, 4));
    }

    private void DrawFooter()
    {
        ImGui.Separator();
        ImGui.Dummy(Ui.S(0, 4));
        var cfg = plugin.Configuration;
        var spacing = ImGui.GetStyle().ItemSpacing.X;

        if (step > 1)
        {
            if (Ui.Button(FontAwesomeIcon.ArrowLeft, "Back"))
                step--;
        }
        else
        {
            using (ImRaii.PushColor(ImGuiCol.Button, Vector4.Zero).Push(ImGuiCol.Text, Ui.Muted))
            {
                if (ImGui.Button("Skip setup"))
                    Finish();
            }
            Ui.Tooltip("Keep the defaults. You can open this again with /lb wizard.");
        }

        if (step < Steps)
        {
            var blocked = step == 1 && cfg.LanguageA == cfg.LanguageB;
            Ui.AlignRight(Ui.IconTextButtonWidth(FontAwesomeIcon.ArrowRight, "Next"));
            using (ImRaii.Disabled(blocked))
            {
                if (Ui.Button(FontAwesomeIcon.ArrowRight, "Next", primary: true,
                              tooltip: blocked ? "Pick two different languages first." : null))
                    step++;
            }
        }
        else
        {
            var w = Ui.IconTextButtonWidth(FontAwesomeIcon.Check, "Finish") +
                    Ui.IconTextButtonWidth(FontAwesomeIcon.Flag, "Finish + guided tour") + spacing;
            Ui.AlignRight(w);
            if (Ui.Button(FontAwesomeIcon.Check, "Finish"))
                Finish(tour: false);
            ImGui.SameLine();
            if (Ui.Button(FontAwesomeIcon.Flag, "Finish + guided tour", primary: true,
                          tooltip: "A one-minute tour of the main window (later: /lb tour)."))
                Finish(tour: true);
        }
    }

    private void Finish(bool tour = false)
    {
        plugin.Configuration.ShowedWizard = true;
        plugin.Translator.ClearCache();
        plugin.SaveConfig();
        IsOpen = false;
        if (tour)
            plugin.Tutorial.StartTour(TourContent.MainTourId);
    }

    private void DrawLanguages()
    {
        var cfg = plugin.Configuration;
        ImGui.TextUnformatted("Welcome to LinguaBridge!");
        Ui.Hint("It translates chat between two languages, in both directions, and lets you send your own " +
                "messages already translated.");

        Ui.Section("Which two languages are spoken in your group?", FontAwesomeIcon.Language);
        var names = Languages.All.Select(l => l.Name).ToArray();
        var a = IndexOf(cfg.LanguageA);
        var b = IndexOf(cfg.LanguageB);
        ImGui.SetNextItemWidth(Ui.S(180));
        if (ImGui.Combo("##wa", ref a, names, names.Length))
            cfg.LanguageA = Languages.All[a].Code;
        ImGui.SameLine();
        Ui.Icon(FontAwesomeIcon.ExchangeAlt, Ui.Muted);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(Ui.S(180));
        if (ImGui.Combo("##wb", ref b, names, names.Length))
            cfg.LanguageB = Languages.All[b].Code;
        if (cfg.LanguageA == cfg.LanguageB)
            Ui.Status("Pick two different languages.", StatusKind.Error);

        Ui.Section("Which language do you read best?", FontAwesomeIcon.Book);
        var p = IndexOf(cfg.PreferredLanguage);
        ImGui.SetNextItemWidth(Ui.S(180));
        if (ImGui.Combo("##wp", ref p, names, names.Length))
            cfg.PreferredLanguage = Languages.All[p].Code;
        Ui.Hint("Incoming chat is translated into this language.");
        var both = !cfg.IncomingOnlyToPreferred;
        if (ImGui.Checkbox("Translate in both directions (I read neither language well)", ref both))
            cfg.IncomingOnlyToPreferred = !both;

        Ui.Section("Help while you learn", FontAwesomeIcon.InfoCircle);
        var tips = cfg.TipsEnabled;
        if (ImGui.Checkbox("Show short tips the first time I use a feature", ref tips))
            cfg.TipsEnabled = tips;
        Ui.Hint("You can turn them off at any time (Settings > Help, or /lb tips off).");
    }

    private void DrawEngine()
    {
        var cfg = plugin.Configuration;
        Ui.Section("Who translates?", FontAwesomeIcon.Language, first: true);

        if (ImGui.RadioButton("DeepL  (recommended, best quality)", cfg.Engine == TranslationEngine.DeepL))
            cfg.Engine = TranslationEngine.DeepL;
        using (ImRaii.PushIndent(ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X))
            Ui.Hint("Free API key: 500,000 characters per month.");
        ImGui.Dummy(Ui.S(0, 2));
        if (ImGui.RadioButton("Google Translate  (no setup)", cfg.Engine == TranslationEngine.Google))
            cfg.Engine = TranslationEngine.Google;
        using (ImRaii.PushIndent(ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X))
            Ui.Hint("Works instantly, less natural with slang and game terms.");

        if (cfg.Engine == TranslationEngine.DeepL)
        {
            Ui.Section("Your DeepL key", FontAwesomeIcon.Key);
            Ui.Callout(FontAwesomeIcon.InfoCircle, Ui.Accent, "Getting a free key (2 minutes)", () =>
            {
                ImGui.TextWrapped("1. deepl.com > Pricing > API > \"DeepL API Free\" (a card is asked for verification, no charge).");
                ImGui.TextWrapped("2. Account > \"API Keys & Limits\", copy the key. Free keys end in \":fx\".");
                if (ImGui.SmallButton("Copy deepl.com/pro-api link"))
                    ImGui.SetClipboardText("https://www.deepl.com/pro-api");
            });
            ImGui.Dummy(Ui.S(0, 4));
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
            Ui.Hint("You can add more keys later in Settings > Engine.");
            var fb = cfg.FallbackToGoogle;
            if (ImGui.Checkbox("Use Google automatically if DeepL fails or runs out", ref fb))
                cfg.FallbackToGoogle = fb;
        }

        Ui.Section("Check that it works", FontAwesomeIcon.CheckCircle);
        using (ImRaii.Disabled(testing))
        {
            if (Ui.Button(FontAwesomeIcon.Magic, "Test translation"))
                _ = Test();
        }
        if (testResult.Length > 0)
            Ui.Status(testResult, testKind);
    }

    private async Task Test()
    {
        testing = true;
        testResult = "Testing...";
        testKind = StatusKind.Busy;
        try
        {
            plugin.Translator.ClearCache();
            var cfg = plugin.Configuration;
            var a = Languages.Get(cfg.LanguageA);
            var b = Languages.Get(cfg.LanguageB);
            var r = await plugin.Translator.TranslateAsync("Thanks for the help, see you tonight!", b.Code == "EN" ? a : b);
            testResult = r == null ? "Works (the text was already in that language)." : $"Works via {r.Engine}: {r.Text}";
            testKind = StatusKind.Success;
        }
        catch (Exception ex)
        {
            testResult = "Failed: " + ex.Message;
            testKind = StatusKind.Error;
        }
        finally
        {
            testing = false;
        }
    }

    private void DrawChannels()
    {
        var cfg = plugin.Configuration;
        Ui.Section("Which chats should be translated automatically?", FontAwesomeIcon.Comments, first: true);
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

        Ui.Section("Where translations appear", FontAwesomeIcon.Eye);
        var same = cfg.PrintInSameChannel;
        if (ImGui.Checkbox("In the same chat tab as the original", ref same))
            cfg.PrintInSameChannel = same;
        Ui.Hint("Otherwise translations go to the Echo channel.");
        var combat = cfg.PauseInCombat;
        if (ImGui.Checkbox("Pause translation while in combat", ref combat))
            cfg.PauseInCombat = combat;
    }

    private static readonly (string Command, string What)[] CheatSheet =
    {
        ("/tr message", "Send translated to the current channel"),
        ("/tr /p ...  /tr /fc ...", "Send translated to party, FC..."),
        ("/tr /t Name@World ...", "Send a translated tell"),
        ("/tr :en ...", "Force the target language"),
        ("/lb", "History, Reply buttons, preview before sending"),
        ("/lb clip", "Translate what you copied"),
        ("/lb config", "All settings"),
        ("/lb wizard  /lb tour", "This setup again / guided tour"),
    };

    private void DrawFinish()
    {
        Ui.Status("All set! Messages from your friends now get a translated line tagged like [EN>FR].", StatusKind.Success);
        Ui.Section("Quick reference", FontAwesomeIcon.Book);
        using (var table = ImRaii.Table("cheat", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.PadOuterX))
        {
            if (table)
            {
                ImGui.TableSetupColumn("cmd", ImGuiTableColumnFlags.WidthFixed, Ui.S(170));
                ImGui.TableSetupColumn("what", ImGuiTableColumnFlags.WidthStretch);
                foreach (var (cmd, what) in CheatSheet)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.TextColored(Ui.Accent, cmd);
                    ImGui.TableNextColumn();
                    ImGui.TextWrapped(what);
                }
            }
        }
        ImGui.Dummy(Ui.S(0, 6));
        Ui.Hint("The \"LB\" entry at the top right of the screen turns translation on/off (left click) or opens the " +
                "window (right click). In Party Finder, right-click a listing > \"Translate description\".");
        ImGui.Dummy(Ui.S(0, 4));
        Ui.Hint("New here? \"Finish + guided tour\" shows you around the window step by step.");
    }

    private static int IndexOf(string code)
    {
        var i = Languages.All.ToList().FindIndex(l => l.Code == code);
        return i < 0 ? 0 : i;
    }
}
