using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using LinguaBridge.Localization;
using static LinguaBridge.Localization.Loc;

namespace LinguaBridge.Windows;

/// <summary>
/// Shown once to existing installs after the update that added interface translations: pick the language of
/// the plugin's menus. Picking a language previews it right away; closing the window keeps the current one.
/// </summary>
public sealed class LanguagePrompt : Window
{
    private readonly Plugin plugin;
    private string selected = "en";

    public LanguagePrompt(Plugin plugin)
        : base("LinguaBridge###LinguaBridgeLanguage",
               ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings)
    {
        this.plugin = plugin;
        PositionCondition = ImGuiCond.Appearing;
    }

    public override void OnOpen() => selected = Loc.Current;

    public override void PreDraw()
    {
        var vp = ImGui.GetMainViewport();
        Position = vp.WorkPos + vp.WorkSize / 2 - Ui.S(190, 150);
    }

    public override void Draw()
    {
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Ui.S(360));
        Ui.Icon(FontAwesomeIcon.Language, Ui.Accent);
        ImGui.SameLine();
        ImGui.TextColored(Ui.Accent, L("LinguaBridge is now available in your language"));
        ImGui.Dummy(Ui.S(0, 2));
        Ui.Hint(L("Choose the language of the plugin's menus, tips and messages. " +
                  "It does not change the languages your chat is translated between."));
        ImGui.Dummy(Ui.S(0, 6));

        Selector(ref selected, Ui.S(360), code =>
        {
            // Live preview: the window redraws in the language being considered.
            Loc.SetLanguage(code);
        });

        ImGui.Dummy(Ui.S(0, 6));
        Ui.Hint(L("You can change it later in Settings > Languages."));
        ImGui.PopTextWrapPos();
        ImGui.Dummy(Ui.S(0, 4));

        var w = Ui.IconTextButtonWidth(FontAwesomeIcon.Check, L("Use this language"));
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Ui.S(360) - w);
        if (Ui.Button(FontAwesomeIcon.Check, L("Use this language"), primary: true))
        {
            plugin.SetUiLanguage(selected);
            IsOpen = false;
        }
    }

    /// <summary>Closed with the X: keep what is selected and don't ask again.</summary>
    public override void OnClose()
    {
        if (!plugin.Configuration.UiLanguageChosen)
            plugin.SetUiLanguage(selected);
    }

    /// <summary>A column of large buttons, one per interface language, each written in its own language.</summary>
    public static bool Selector(ref string current, float width, System.Action<string>? onChange = null)
    {
        var changed = false;
        foreach (var (code, native) in UiLanguages)
        {
            var on = code == current;
            using (Dalamud.Interface.Utility.Raii.ImRaii.PushColor(ImGuiCol.Button, Ui.Accent with { W = 0.55f }, on)
                         .Push(ImGuiCol.ButtonHovered, Ui.Accent with { W = 0.70f }, on)
                         .Push(ImGuiCol.Text, Ui.Muted, !on))
            {
                if (ImGui.Button($"{native}##{code}", new Vector2(width, ImGui.GetFrameHeight() * 1.3f)) && !on)
                {
                    current = code;
                    changed = true;
                    onChange?.Invoke(code);
                }
            }
        }
        return changed;
    }
}
