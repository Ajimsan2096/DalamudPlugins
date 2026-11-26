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

public sealed partial class ConfigWindow : Window
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

    private static int IndexOf(string code)
    {
        var i = Languages.All.ToList().FindIndex(l => l.Code == code);
        return i < 0 ? 0 : i;
    }
}
