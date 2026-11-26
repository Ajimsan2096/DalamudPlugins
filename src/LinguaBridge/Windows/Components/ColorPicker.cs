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

/// <summary>Picks one of the game's text colours (UIColor sheet): a swatch that opens a palette.</summary>
public static class ColorPicker
{
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
    public static bool Draw(string id, string label, ref ushort value)
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
