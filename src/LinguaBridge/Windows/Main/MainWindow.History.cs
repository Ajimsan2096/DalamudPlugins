using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using LinguaBridge.Chat;
using LinguaBridge.Translation;
using LinguaBridge.Tutorial;
using static LinguaBridge.Localization.Loc;

namespace LinguaBridge.Windows;

/// <summary>The translation history: one card per message, with copy / reply / translate actions and a filter.</summary>
public sealed partial class MainWindow
{
    private void DrawHistory(IReadOnlyList<HistoryEntry> history)
    {
        if (history.Count > 0)
        {
            Ui.Icon(FontAwesomeIcon.Search, Ui.Muted);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(Ui.S(220));
            ImGui.InputTextWithHint("##hfilter", L("Filter by player or text..."), ref historyFilter, 60);
            if (historyFilter.Length > 0)
            {
                ImGui.SameLine();
                if (Ui.GhostIconButton("clearfilter", FontAwesomeIcon.Times, L("Clear the filter")))
                    historyFilter = string.Empty;
            }
        }
        var reserve = composeHeight > 0 ? composeHeight : ImGui.GetFrameHeightWithSpacing() * 5;
        using (ImRaii.PushColor(ImGuiCol.ChildBg, ImGui.GetStyle().Colors[(int)ImGuiCol.FrameBg] with { W = 0.35f }))
        using (ImRaii.PushStyle(ImGuiStyleVar.ChildRounding, Ui.Rounding))
        using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Ui.S(8, 8)))
        {
            using var child = ImRaii.Child("history", new Vector2(0, -reserve), false, ImGuiWindowFlags.AlwaysUseWindowPadding);
            if (!child)
                return;
            plugin.Tutorial.AnchorWindow("main.history");

            if (history.Count == 0)
            {
                DrawEmptyHistory();
                return;
            }

            // Newest is on top: jump there when something new arrives, unless you scrolled down to read.
            var version = plugin.HistoryVersion;
            if (version != lastHistoryVersion)
            {
                if (ImGui.GetScrollY() <= Ui.S(4))
                    ImGui.SetScrollY(0);
                lastHistoryVersion = version;
            }

            var i = 0;
            var shown = 0;
            foreach (var e in history)
            {
                if (historyFilter.Length > 0 && !Matches(e, historyFilter))
                    continue;
                using var id = ImRaii.PushId(i++);
                DrawEntry(e);
                ImGui.Dummy(Ui.S(0, 4));
                shown++;
            }
            if (shown == 0)
                Ui.MutedText(L("No message matches the filter."));
        }
    }

    private static bool Matches(HistoryEntry e, string filter) =>
        e.Sender.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
        e.Translation.Text.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
        e.Translation.Original.Contains(filter, StringComparison.OrdinalIgnoreCase);

    /// <summary>Player names are shown as they are; "You", "Clipboard", "Listing" are translated.</summary>
    private static string SenderLabel(HistoryEntry e) => e.FromPlayer ? e.Sender : L(e.Sender);

    private static void DrawEmptyHistory()
    {
        var avail = ImGui.GetContentRegionAvail();
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + Math.Max(0, avail.Y / 2 - ImGui.GetTextLineHeight() * 3));
        using var font = ImRaii.PushFont(UiBuilder.IconFont);
        Centered(FontAwesomeIcon.Comments.ToIconString(), Ui.Muted);
        font.Dispose();
        ImGui.Spacing();
        Centered(L("No translations yet"), ImGui.GetStyle().Colors[(int)ImGuiCol.Text]);
        Centered(L("Messages from the chats you selected will appear here."), Ui.Muted);
        Centered(L("Press Reply on one to answer it translated, in the same channel."), Ui.Muted);
    }

    private static void Centered(string text, Vector4 color)
    {
        var w = ImGui.CalcTextSize(text).X;
        ImGui.SetCursorPosX(Math.Max(ImGui.GetCursorPosX(), (ImGui.GetWindowWidth() - w) / 2));
        ImGui.TextColored(color, text);
    }

    /// <summary>One message as a card: who / where / when, the translation, then the original in muted text.</summary>
    private void DrawEntry(HistoryEntry e)
    {
        var dl = ImGui.GetWindowDrawList();
        var start = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var pad = Ui.S(10, 6);
        var edge = e.Outgoing ? Ui.Accent : Ui.Incoming;

        dl.ChannelsSplit(2);
        dl.ChannelsSetCurrent(1);
        ImGui.SetCursorScreenPos(start + pad);
        using (ImRaii.Group())
        {
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width - pad.X * 2);

            // Header: direction icon, sender, channel, language pair ... time and actions on the right.
            Ui.Icon(e.Outgoing ? FontAwesomeIcon.ArrowUp : FontAwesomeIcon.ArrowDown, edge);
            Ui.Tooltip(e.Outgoing ? L("Sent by you") : L("Received"));
            ImGui.SameLine();
            ImGui.TextColored(edge, SenderLabel(e));
            ImGui.SameLine();
            Ui.Badge(L(e.Channel), Ui.Muted);
            ImGui.SameLine();
            if (e.Skipped)
                Ui.Badge(L("not translated"), Ui.Warning, L("Longer than the maximum message length (Settings > Chat / Roleplay)."));
            else
                Ui.Badge($"{e.Translation.Source.Code} > {e.Translation.Target.Code}", edge, // loc-ok: codes / numbers only
                         LF("{0} to {1} ({2})", L(e.Translation.Source.Name), L(e.Translation.Target.Name), e.Translation.Engine));

            var time = $"{e.Time:HH:mm}";
            var spacing = ImGui.GetStyle().ItemSpacing.X;
            var actions = ImGui.CalcTextSize(time).X + spacing + Ui.IconButtonWidth(FontAwesomeIcon.Copy) +
                          (e.ReplyPrefix != null ? spacing + Ui.IconButtonWidth(FontAwesomeIcon.Reply) : 0);
            ImGui.SameLine();
            var x = start.X - ImGui.GetWindowPos().X + width - pad.X - actions;
            if (x > ImGui.GetCursorPosX())
                ImGui.SetCursorPosX(x);
            var y = ImGui.GetCursorPosY();
            ImGui.SetCursorPosY(y - ImGui.GetStyle().FramePadding.Y);
            using (ImRaii.Group())
            {
                ImGui.AlignTextToFramePadding();
                ImGui.TextColored(Ui.Muted, time);
                ImGui.SameLine();
                if (Ui.GhostIconButton("copy", FontAwesomeIcon.Copy, L("Copy the translation")))
                {
                    ImGui.SetClipboardText(e.Translation.Text);
                    SetStatus(L("Translation copied to the clipboard."), StatusKind.Success);
                }
                if (e.ReplyPrefix != null)
                {
                    ImGui.SameLine();
                    if (Ui.GhostIconButton("reply", FontAwesomeIcon.Reply, LF("Reply to {0} translated, in {1}", SenderLabel(e), L(e.Channel))))
                    {
                        replyPrefix = e.ReplyPrefix;
                        replyLabel = $"{SenderLabel(e)} ({L(e.Channel)})";
                        focusInput = true;
                        plugin.Tutorial.ShowTip(TourContent.FirstReply);
                    }
                }
            }

            // Body.
            ImGui.Dummy(Ui.S(0, 1));
            if (e.Skipped)
            {
                ImGui.TextWrapped(e.Translation.Text);
                using (ImRaii.Disabled(busy))
                {
                    if (Ui.Button(FontAwesomeIcon.Language, L("Translate")))
                        _ = TranslateSkipped(e);
                }
                ImGui.PopTextWrapPos();
                goto done;
            }
            ImGui.TextWrapped(e.Translation.Text);
            if (e.Learning is { } learning)
            {
                using (ImRaii.PushColor(ImGuiCol.Text, Ui.Learning))
                    ImGui.TextWrapped($"[{learning.Target.Code}] {learning.Text}");
                if (ImGui.IsItemClicked())
                {
                    ImGui.SetClipboardText(learning.Text);
                    SetStatus(L("Learning line copied to the clipboard."), StatusKind.Success);
                }
                Ui.Tooltip(LF("Learning language ({0}). Click to copy.", L(learning.Target.Name)));
            }
            using (ImRaii.PushColor(ImGuiCol.Text, Ui.Muted))
                ImGui.TextWrapped(e.Translation.Original);
            Ui.Tooltip(L("Original message"));

            ImGui.PopTextWrapPos();
            done: ;
        }
        var end = new Vector2(start.X + width, ImGui.GetItemRectMax().Y + pad.Y);

        dl.ChannelsSetCurrent(0);
        var hovered = ImGui.IsMouseHoveringRect(start, end) && ImGui.IsWindowHovered();
        var bg = ImGui.GetStyle().Colors[(int)ImGuiCol.WindowBg];
        dl.AddRectFilled(start, end, ImGui.GetColorU32(hovered ? edge with { W = 0.08f } : bg with { W = 0.55f }), Ui.Rounding);
        dl.AddRectFilled(start, new Vector2(start.X + Ui.S(3), end.Y), ImGui.GetColorU32(edge with { W = 0.85f }), Ui.Rounding,
                         ImDrawFlags.RoundCornersLeft);
        dl.ChannelsMerge();

        ImGui.SetCursorScreenPos(start);
        ImGui.Dummy(end - start);
    }
}
