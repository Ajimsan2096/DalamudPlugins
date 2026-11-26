using System;
using System.Collections.Generic;
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
using static LinguaBridge.Localization.Loc;

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
    private bool finished;

    /// <summary>The test sentence, written in each language (the wizard translates it from language A to B).</summary>
    private static readonly Dictionary<string, string> TestSentences = new()
    {
        ["EN"] = "Thanks for the help, see you tonight!",
        ["FR"] = "Merci pour l'aide, à ce soir !",
        ["ES"] = "¡Gracias por la ayuda, nos vemos esta noche!",
        ["DE"] = "Danke für die Hilfe, bis heute Abend!",
        ["IT"] = "Grazie per l'aiuto, ci vediamo stasera!",
        ["PT"] = "Obrigado pela ajuda, até hoje à noite!",
        ["NL"] = "Bedankt voor de hulp, tot vanavond!",
        ["JA"] = "手伝ってくれてありがとう、また今夜ね！",
        ["RU"] = "Спасибо за помощь, увидимся вечером!",
    };

    public SetupWizard(Plugin plugin) : base("LinguaBridge setup###LinguaBridgeWizard", ImGuiWindowFlags.NoCollapse)
    {
        this.plugin = plugin;
        Size = new Vector2(540, 500);
        SizeCondition = ImGuiCond.Appearing;
    }

    public override void OnOpen()
    {
        step = 1;
        finished = false;
    }

    /// <summary>Closed with the X: keep what was chosen so far (it would otherwise come back at every start).</summary>
    public override void OnClose()
    {
        if (finished || plugin.Configuration.ShowedWizard)
            return;
        Finish();
        Plugin.ChatGui.Print(L("Setup closed: your choices so far are kept. /lb wizard opens it again."), "LinguaBridge");
    }

    public override void PreDraw() => WindowName = L("LinguaBridge setup") + "###LinguaBridgeWizard";

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
            var label = L(StepNames[i - 1]);
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
            if (Ui.Button(FontAwesomeIcon.ArrowLeft, L("Back")))
                step--;
        }
        else
        {
            using (ImRaii.PushColor(ImGuiCol.Button, Vector4.Zero).Push(ImGuiCol.Text, Ui.Muted))
            {
                if (ImGui.Button(L("Skip setup")))
                    Finish();
            }
            Ui.Tooltip(L("Keep the defaults. You can open this again with /lb wizard."));
        }

        if (step < Steps)
        {
            var blocked = step == 1 && cfg.LanguageA == cfg.LanguageB;
            Ui.AlignRight(Ui.IconTextButtonWidth(FontAwesomeIcon.ArrowRight, L("Next")));
            using (ImRaii.Disabled(blocked))
            {
                if (Ui.Button(FontAwesomeIcon.ArrowRight, L("Next"), primary: true,
                              tooltip: blocked ? L("Pick two different languages first.") : null))
                    step++;
            }
        }
        else
        {
            var w = Ui.IconTextButtonWidth(FontAwesomeIcon.Check, L("Finish")) +
                    Ui.IconTextButtonWidth(FontAwesomeIcon.Flag, L("Finish + guided tour")) + spacing;
            Ui.AlignRight(w);
            if (Ui.Button(FontAwesomeIcon.Check, L("Finish")))
                Finish(tour: false);
            ImGui.SameLine();
            if (Ui.Button(FontAwesomeIcon.Flag, L("Finish + guided tour"), primary: true,
                          tooltip: L("A one-minute tour of the main window (later: /lb tour).")))
                Finish(tour: true);
        }
    }

    /// <summary>The four interface languages side by side, each written in its own language.</summary>
    internal static bool LanguageButtons(ref string current)
    {
        var langs = Localization.Loc.UiLanguages;
        var labels = System.Array.ConvertAll(langs, l => l.Native);
        var code = current;
        var index = System.Math.Max(0, System.Array.FindIndex(langs, l => l.Code == code));
        if (!Ui.Segmented("uilang", labels, ref index))
            return false;
        current = langs[index].Code;
        return true;
    }

    private void Finish(bool tour = false)
    {
        finished = true;
        plugin.Configuration.ShowedWizard = true;
        if (!plugin.Configuration.UiLanguageChosen)
            plugin.SetUiLanguage(Localization.Loc.Current);
        plugin.Translator.ClearCache();
        plugin.SaveConfig();
        IsOpen = false;
        if (tour)
            plugin.Tutorial.StartTour(TourContent.MainTourId);
    }

    private void DrawLanguages()
    {
        var cfg = plugin.Configuration;
        ImGui.TextUnformatted(L("Welcome to LinguaBridge!"));
        Ui.Hint(L("It translates chat between two languages, in both directions, and lets you send your own " +
                "messages already translated."));

        Ui.Section(L("Plugin language"), FontAwesomeIcon.Language,
                   help: L("Language of these menus, tips and messages. It does not change the languages your chat is translated between."));
        var ui = Localization.Loc.Current;
        if (LanguageButtons(ref ui))
            plugin.SetUiLanguage(ui);

        Ui.Section(L("Which two languages are spoken in your group?"), FontAwesomeIcon.Language);
        var names = Languages.All.Select(l => L(l.Name)).ToArray();
        var a = IndexOf(cfg.LanguageA);
        var b = IndexOf(cfg.LanguageB);
        ImGui.SetNextItemWidth(Ui.S(180));
        if (ImGui.Combo("##wa", ref a, names, names.Length))
        {
            if (Languages.All[a].Code == cfg.LanguageB)
                cfg.LanguageB = cfg.LanguageA; // the same language twice: swap them
            cfg.LanguageA = Languages.All[a].Code;
        }
        ImGui.SameLine();
        Ui.Icon(FontAwesomeIcon.ExchangeAlt, Ui.Muted);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(Ui.S(180));
        if (ImGui.Combo("##wb", ref b, names, names.Length))
        {
            if (Languages.All[b].Code == cfg.LanguageA)
                cfg.LanguageA = cfg.LanguageB;
            cfg.LanguageB = Languages.All[b].Code;
        }
        if (cfg.LanguageA == cfg.LanguageB)
            Ui.Status(L("Pick two different languages."), StatusKind.Error);

        Ui.Section(L("Which language do you read best?"), FontAwesomeIcon.Book);
        var p = IndexOf(cfg.PreferredLanguage);
        ImGui.SetNextItemWidth(Ui.S(180));
        if (ImGui.Combo("##wp", ref p, names, names.Length))
            cfg.PreferredLanguage = Languages.All[p].Code;
        Ui.Hint(L("Incoming chat is translated into this language."));
        var both = !cfg.IncomingOnlyToPreferred;
        if (ImGui.Checkbox(L("Translate in both directions (I read neither language well)"), ref both))
            cfg.IncomingOnlyToPreferred = !both;

        Ui.Section(L("Languages you already understand"), FontAwesomeIcon.Filter,
                   help: L("Messages in these languages are not translated. You can change it later in Settings > Languages."));
        using (var table = ImRaii.Table("##wunderstood", 3, ImGuiTableFlags.SizingStretchSame))
        {
            if (table)
            {
                foreach (var lang in Languages.All)
                {
                    ImGui.TableNextColumn();
                    if (cfg.IncomingOnlyToPreferred && lang.Code == cfg.PreferredLanguage)
                    {
                        var on = true;
                        using (ImRaii.Disabled())
                            ImGui.Checkbox($"{L(lang.Name)}##wu{lang.Code}", ref on);
                        Ui.Tooltip(L("The language you read best is never translated."));
                        continue;
                    }
                    var ticked = cfg.UnderstoodLanguages.Contains(lang.Code);
                    if (ImGui.Checkbox($"{L(lang.Name)}##wu{lang.Code}", ref ticked))
                    {
                        cfg.LanguageMode = IncomingLanguageMode.AllExcept;
                        if (ticked) cfg.UnderstoodLanguages.Add(lang.Code);
                        else cfg.UnderstoodLanguages.RemoveAll(c => c == lang.Code);
                    }
                }
            }
        }

        Ui.Section(L("Help while you learn"), FontAwesomeIcon.InfoCircle);
        var tips = cfg.TipsEnabled;
        if (ImGui.Checkbox(L("Show short tips the first time I use a feature"), ref tips))
            cfg.TipsEnabled = tips;
        Ui.Hint(L("You can turn them off at any time (Settings > Help, or /lb tips off)."));
    }

    private void DrawEngine()
    {
        var cfg = plugin.Configuration;
        Ui.Section(L("Who translates?"), FontAwesomeIcon.Language, first: true);

        if (ImGui.RadioButton(L("DeepL  (recommended, best quality)"), cfg.Engine == TranslationEngine.DeepL))
            cfg.Engine = TranslationEngine.DeepL;
        using (ImRaii.PushIndent(ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X))
            Ui.Hint(L("Free API key: 500,000 characters per month."));
        ImGui.Dummy(Ui.S(0, 2));
        if (ImGui.RadioButton(L("Google Translate  (no setup)"), cfg.Engine == TranslationEngine.Google))
            cfg.Engine = TranslationEngine.Google;
        using (ImRaii.PushIndent(ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X))
            Ui.Hint(L("Works instantly, less natural with slang and game terms."));

        if (cfg.Engine == TranslationEngine.DeepL)
        {
            Ui.Section(L("Your DeepL key"), FontAwesomeIcon.Key);
            Ui.Callout(FontAwesomeIcon.InfoCircle, Ui.Accent, L("Getting a free key (2 minutes)"), () =>
            {
                ImGui.TextWrapped(L("1. deepl.com > Pricing > API > \"DeepL API Free\" (a card is asked for verification, no charge)."));
                ImGui.TextWrapped(L("2. Account > \"API Keys & Limits\", copy the key. Free keys end in \":fx\"."));
                if (ImGui.SmallButton(L("Copy deepl.com/pro-api link")))
                    ImGui.SetClipboardText("https://www.deepl.com/pro-api");
            });
            ImGui.Dummy(Ui.S(0, 4));
            var key = cfg.DeepLApiKeys.Count > 0 ? cfg.DeepLApiKeys[0] : string.Empty;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputTextWithHint("##wkey", L("Paste your DeepL API key here"), ref key, 128, ImGuiInputTextFlags.Password))
            {
                if (cfg.DeepLApiKeys.Count == 0)
                    cfg.DeepLApiKeys.Add(key.Trim());
                else
                    cfg.DeepLApiKeys[0] = key.Trim();
                plugin.Translator.DeepL.ResetKeyStates();
            }
            Ui.Hint(L("You can add more keys later in Settings > Engine."));
            var fb = cfg.FallbackToGoogle;
            if (ImGui.Checkbox(L("Use Google automatically if DeepL fails or runs out"), ref fb))
                cfg.FallbackToGoogle = fb;
        }

        Ui.Section(L("Check that it works"), FontAwesomeIcon.CheckCircle);
        using (ImRaii.Disabled(testing))
        {
            if (Ui.Button(FontAwesomeIcon.Magic, L("Test translation")))
                _ = Test();
        }
        if (testResult.Length > 0)
            Ui.Status(testResult, testKind);
    }

    private async Task Test()
    {
        testing = true;
        testResult = L("Testing...");
        testKind = StatusKind.Busy;
        try
        {
            plugin.Translator.ClearCache();
            var cfg = plugin.Configuration;
            var a = Languages.Get(cfg.LanguageA);
            var b = Languages.Get(cfg.LanguageB);
            // A sentence in language A, translated into B: a real translation whatever the pair.
            var r = await plugin.Translator.TranslateAsync(TestSentences.GetValueOrDefault(a.Code, TestSentences["EN"]), b);
            if (r == null)
            {
                testResult = L("No translation came back. Check the languages and the engine.");
                testKind = StatusKind.Warning;
            }
            else
            {
                testResult = LF("Works via {0}: {1}", r.Engine, r.Text);
                testKind = StatusKind.Success;
            }
        }
        catch (Exception ex)
        {
            testResult = L("Failed: ") + ex.Message;
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
        Ui.Section(L("Which chats should be translated automatically?"), FontAwesomeIcon.Comments, first: true);
        foreach (var (label, types) in ChannelPresets)
        {
            var on = types.All(cfg.Channels.Contains);
            if (ImGui.Checkbox(L(label), ref on))
                foreach (var t in types)
                {
                    if (on) cfg.Channels.Add(t);
                    else cfg.Channels.Remove(t);
                }
        }

        Ui.Section(L("Where translations appear"), FontAwesomeIcon.Eye);
        var same = cfg.PrintInSameChannel;
        if (ImGui.Checkbox(L("In the same chat tab as the original"), ref same))
            cfg.PrintInSameChannel = same;
        Ui.Hint(L("Otherwise translations go to the Echo channel."));
        var combat = cfg.PauseInCombat;
        if (ImGui.Checkbox(L("Pause translation while in combat"), ref combat))
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
        Ui.Status(L("All set! Messages from your friends now get a translated line tagged like [EN>FR]."), StatusKind.Success);
        Ui.Section(L("Quick reference"), FontAwesomeIcon.Book);
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
                    ImGui.TextWrapped(L(what));
                }
            }
        }
        ImGui.Dummy(Ui.S(0, 6));
        Ui.Hint(L("The \"LB\" entry at the top right of the screen turns translation on/off (left click) or opens the " +
                "window (right click). In Party Finder, right-click a listing > \"Translate description\"."));
        ImGui.Dummy(Ui.S(0, 4));
        Ui.Hint(L("New here? \"Finish + guided tour\" shows you around the window step by step."));
    }

    private static int IndexOf(string code)
    {
        var i = Languages.All.ToList().FindIndex(l => l.Code == code);
        return i < 0 ? 0 : i;
    }
}
