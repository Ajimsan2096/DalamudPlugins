using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace LinguaBridge.Windows;

/// <summary>Kind of a status line: picks its icon and colour (never colour alone, for colour-blind users).</summary>
public enum StatusKind
{
    None,
    Info,
    Busy,
    Success,
    Warning,
    Error,
}

/// <summary>
/// Small design system shared by every LinguaBridge window: colour tokens, DPI-aware sizes and a few
/// components (section headers, icon buttons with tooltips, badges, callouts, status lines).
/// Keeps the windows consistent and readable at any Dalamud font scale.
/// </summary>
internal static class Ui
{
    // ---------------------------------------------------------------- colour tokens
    // Chosen for contrast on dark ImGui themes (all >= 4.5:1 on the default window background).

    /// <summary>Brand / primary actions / outgoing messages.</summary>
    public static readonly Vector4 Accent = new(0.40f, 0.78f, 1f, 1f);
    /// <summary>Incoming messages.</summary>
    public static readonly Vector4 Incoming = new(1f, 0.80f, 0.45f, 1f);
    public static readonly Vector4 Success = new(0.45f, 0.90f, 0.55f, 1f);
    public static readonly Vector4 Warning = new(1f, 0.72f, 0.35f, 1f);
    public static readonly Vector4 Danger = new(1f, 0.47f, 0.47f, 1f);
    /// <summary>Learning-language line.</summary>
    public static readonly Vector4 Learning = new(0.60f, 0.92f, 0.62f, 1f);

    /// <summary>Secondary text. Brighter than ImGui's TextDisabled, which is too faint for text people must read.</summary>
    public static Vector4 Muted
    {
        get
        {
            var t = ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
            return t with { W = t.W * 0.68f };
        }
    }

    // ---------------------------------------------------------------- sizes

    /// <summary>Scales a pixel size by the user's Dalamud global font scale.</summary>
    public static float S(float px) => px * ImGuiHelpers.GlobalScale;

    public static Vector2 S(float x, float y) => new Vector2(x, y) * ImGuiHelpers.GlobalScale;

    /// <summary>Standard width of combos and short inputs.</summary>
    public static float FieldWidth => S(220);

    public static float Rounding => S(6);

    // ---------------------------------------------------------------- text

    public static void MutedText(string text) => ImGui.TextColored(Muted, text);

    /// <summary>Wrapped secondary text, for explanations under a setting.</summary>
    public static void Hint(string text)
    {
        using (ImRaii.PushColor(ImGuiCol.Text, Muted))
            ImGui.TextWrapped(text);
    }

    public static void Icon(FontAwesomeIcon icon, Vector4? color = null)
    {
        using var font = ImRaii.PushFont(UiBuilder.IconFont);
        if (color is { } c)
            ImGui.TextColored(c, icon.ToIconString());
        else
            ImGui.TextUnformatted(icon.ToIconString());
    }

    /// <summary>Section title: accent icon + label, a thin rule and breathing room.</summary>
    public static void Section(string title, FontAwesomeIcon icon, bool first = false, string? help = null)
    {
        if (!first)
            ImGui.Dummy(S(0, 10));
        Icon(icon, Accent);
        ImGui.SameLine();
        ImGui.TextUnformatted(title);
        if (help != null)
        {
            ImGui.SameLine();
            ImGuiComponents.HelpMarker(help);
        }
        var dl = ImGui.GetWindowDrawList();
        var y = ImGui.GetItemRectMax().Y + S(4);
        var x = ImGui.GetCursorScreenPos().X;
        var w = ImGui.GetContentRegionAvail().X;
        dl.AddLine(new Vector2(x, y), new Vector2(x + w, y), ImGui.GetColorU32(Accent with { W = 0.35f }), S(1));
        ImGui.Dummy(S(0, 6));
    }

    /// <summary>Icon + coloured message. Busy shows an animated spinner.</summary>
    public static void Status(string text, StatusKind kind)
    {
        if (kind == StatusKind.None || text.Length == 0)
            return;
        var color = KindColor(kind);
        if (kind == StatusKind.Busy)
        {
            Spinner(color);
        }
        else
        {
            Icon(KindIcon(kind), color);
        }
        ImGui.SameLine();
        using (ImRaii.PushColor(ImGuiCol.Text, kind == StatusKind.Info ? Muted : color))
            ImGui.TextWrapped(text);
    }

    public static Vector4 KindColor(StatusKind kind) => kind switch
    {
        StatusKind.Success => Success,
        StatusKind.Warning => Warning,
        StatusKind.Error => Danger,
        StatusKind.Busy => Accent,
        _ => Muted,
    };

    private static FontAwesomeIcon KindIcon(StatusKind kind) => kind switch
    {
        StatusKind.Success => FontAwesomeIcon.CheckCircle,
        StatusKind.Warning => FontAwesomeIcon.ExclamationTriangle,
        StatusKind.Error => FontAwesomeIcon.TimesCircle,
        _ => FontAwesomeIcon.InfoCircle,
    };

    /// <summary>A small rotating arc the size of a text line.</summary>
    public static void Spinner(Vector4 color)
    {
        var h = ImGui.GetTextLineHeight();
        var pos = ImGui.GetCursorScreenPos();
        var r = h * 0.38f;
        var c = pos + new Vector2(h / 2, h / 2);
        var t = (float)ImGui.GetTime() * 6f;
        var dl = ImGui.GetWindowDrawList();
        dl.PathArcTo(c, r, t, t + MathF.PI * 1.4f, 16);
        dl.PathStroke(ImGui.GetColorU32(color), ImDrawFlags.None, Math.Max(1.5f, S(2)));
        ImGui.Dummy(new Vector2(h, h));
    }

    // ---------------------------------------------------------------- buttons

    /// <summary>Tooltip on the last item, also while it is disabled (so people learn why).</summary>
    public static void Tooltip(string text)
    {
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(text);
    }

    /// <summary>Square icon button. Always has a tooltip: icons alone are not self-explanatory.</summary>
    public static bool IconButton(string id, FontAwesomeIcon icon, string tooltip, Vector4? iconColor = null)
    {
        bool clicked;
        using (ImRaii.PushId(id))
        using (ImRaii.PushColor(ImGuiCol.Text, iconColor ?? Vector4.Zero, iconColor != null))
            clicked = ImGuiComponents.IconButton(icon);
        Tooltip(tooltip);
        return clicked;
    }

    /// <summary>Frameless icon button for low-emphasis row actions (copy, reply, remove...).</summary>
    public static bool GhostIconButton(string id, FontAwesomeIcon icon, string tooltip, Vector4? hoverColor = null)
    {
        bool clicked;
        using (ImRaii.PushColor(ImGuiCol.Button, Vector4.Zero)
                     .Push(ImGuiCol.ButtonHovered, (hoverColor ?? Accent) with { W = 0.22f })
                     .Push(ImGuiCol.ButtonActive, (hoverColor ?? Accent) with { W = 0.35f }))
            clicked = IconButton(id, icon, tooltip);
        return clicked;
    }

    public static float IconButtonWidth(FontAwesomeIcon icon)
    {
        float w;
        using (ImRaii.PushFont(UiBuilder.IconFont))
            w = ImGui.CalcTextSize(icon.ToIconString()).X;
        return w + ImGui.GetStyle().FramePadding.X * 2;
    }

    public static float IconTextButtonWidth(FontAwesomeIcon icon, string text) =>
        ImGuiComponents.GetIconButtonWithTextWidth(icon, text);

    /// <summary>Icon + label button; <paramref name="primary"/> uses the accent colour (one per area).</summary>
    public static bool Button(FontAwesomeIcon icon, string text, bool primary = false, string? tooltip = null)
    {
        bool clicked;
        using (ImRaii.PushColor(ImGuiCol.Button, Accent with { W = 0.55f }, primary)
                     .Push(ImGuiCol.ButtonHovered, Accent with { W = 0.75f }, primary)
                     .Push(ImGuiCol.ButtonActive, Accent with { W = 0.90f }, primary))
            clicked = ImGuiComponents.IconButtonWithText(icon, text);
        if (tooltip != null)
            Tooltip(tooltip);
        return clicked;
    }

    /// <summary>Moves the cursor so that an item of <paramref name="width"/> ends at the right edge of the line.</summary>
    public static void AlignRight(float width)
    {
        var x = ImGui.GetWindowContentRegionMax().X - width;
        ImGui.SameLine(Math.Max(ImGui.GetCursorPosX(), x));
    }

    /// <summary>Segmented control: a row of joined buttons, the selected one highlighted. Returns true when it changes.</summary>
    public static bool Segmented(string id, string[] labels, ref int selected, string?[]? tooltips = null)
    {
        var changed = false;
        using var _ = ImRaii.PushId(id);
        using var spacing = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, ImGui.GetStyle().ItemSpacing with { X = S(1) });
        for (var i = 0; i < labels.Length; i++)
        {
            if (i > 0)
                ImGui.SameLine();
            var on = i == selected;
            using (ImRaii.PushColor(ImGuiCol.Button, Accent with { W = 0.55f }, on)
                         .Push(ImGuiCol.ButtonHovered, Accent with { W = 0.65f }, on)
                         .Push(ImGuiCol.Text, Muted, !on))
            {
                if (ImGui.Button(labels[i]) && !on)
                {
                    selected = i;
                    changed = true;
                }
            }
            if (tooltips?[i] is { } tip)
                Tooltip(tip);
        }
        return changed;
    }

    // ---------------------------------------------------------------- containers

    /// <summary>Small rounded pill with tinted background, e.g. a channel or "EN > FR".</summary>
    public static void Badge(string text, Vector4 color, string? tooltip = null)
    {
        var size = ImGui.CalcTextSize(text);
        var pad = S(6, 1);
        var pos = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(pos, pos + size + pad * 2, ImGui.GetColorU32(color with { W = 0.16f }), S(4));
        dl.AddText(pos + pad, ImGui.GetColorU32(color), text);
        ImGui.Dummy(size + pad * 2);
        if (tooltip != null)
            Tooltip(tooltip);
    }

    /// <summary>
    /// Tinted box with a coloured left edge, for notes that matter (privacy, warnings, previews).
    /// Not usable inside tables (they own the draw-list channels).
    /// </summary>
    public static void Callout(FontAwesomeIcon icon, Vector4 color, string? title, Action body)
    {
        var dl = ImGui.GetWindowDrawList();
        var start = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var pad = S(10, 8);

        dl.ChannelsSplit(2);
        dl.ChannelsSetCurrent(1);
        ImGui.SetCursorScreenPos(start + pad + new Vector2(S(2), 0));
        using (ImRaii.Group())
        {
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width - pad.X * 2 - S(2));
            if (title != null)
            {
                Icon(icon, color);
                ImGui.SameLine();
                ImGui.TextColored(color, title);
            }
            body();
            ImGui.PopTextWrapPos();
        }
        var end = new Vector2(start.X + width, ImGui.GetItemRectMax().Y + pad.Y);

        dl.ChannelsSetCurrent(0);
        dl.AddRectFilled(start, end, ImGui.GetColorU32(color with { W = 0.09f }), Rounding);
        dl.AddRectFilled(start, new Vector2(start.X + S(3), end.Y), ImGui.GetColorU32(color with { W = 0.85f }), Rounding,
                         ImDrawFlags.RoundCornersLeft);
        dl.ChannelsMerge();

        ImGui.SetCursorScreenPos(start);
        ImGui.Dummy(end - start);
    }

    public static void Callout(FontAwesomeIcon icon, Vector4 color, string title, string text) =>
        Callout(icon, color, title, () => ImGui.TextWrapped(text));
}
