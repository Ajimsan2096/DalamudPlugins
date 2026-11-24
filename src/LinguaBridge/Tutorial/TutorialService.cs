using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using LinguaBridge.Windows;

namespace LinguaBridge.Tutorial;

/// <summary>
/// Guided tours (a bubble next to the highlighted UI element, step by step) and one-time contextual tips.
/// Windows report where their elements are with <see cref="Anchor()"/>; <see cref="Draw"/> runs after them.
/// Everything can be skipped: "Skip tour" in a bubble, "Turn off tips" in a tip, or Settings > Help.
/// </summary>
public sealed class TutorialService
{
    private static Vector4 Accent => Ui.Accent;
    private static Vector4 Good => Ui.Success;
    private static float BubbleWidth => Ui.S(360);
    private static float Gap => Ui.S(14);

    private readonly Plugin plugin;
    private readonly Dictionary<string, AnchorRect> anchors = new();
    private readonly ConcurrentQueue<Tip> pendingTips = new();
    private readonly ConcurrentDictionary<string, byte> queuedTips = new();

    private Tour? tour;
    private int step;
    private bool stepEntered;
    private bool focusBubble;
    private string? requestedTab;
    private Tip? tip;
    private Vector2 bubbleSize = new(360, 160);

    private readonly record struct AnchorRect(Vector2 Min, Vector2 Max, int Frame);

    public TutorialService(Plugin plugin) => this.plugin = plugin;

    public bool TourRunning => tour != null;
    private Configuration Config => plugin.Configuration;
    private bool Active => tour != null || tip != null;

    // ---------------------------------------------------------------- anchors (called by the windows while drawing)

    /// <summary>Remembers the rectangle of the last ImGui item under <paramref name="key"/>.</summary>
    public void Anchor(string key)
    {
        if (Active)
            anchors[key] = new AnchorRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), ImGui.GetFrameCount());
    }

    /// <summary>Remembers the current window's rectangle (call inside Begin/End or a child).</summary>
    public void AnchorWindow(string key)
    {
        if (!Active)
            return;
        var pos = ImGui.GetWindowPos();
        anchors[key] = new AnchorRect(pos, pos + ImGui.GetWindowSize(), ImGui.GetFrameCount());
    }

    /// <summary>True once, when the tour asks the settings window to switch to <paramref name="tab"/>.</summary>
    public bool ConsumeTabRequest(string tab)
    {
        if (requestedTab != tab)
            return false;
        requestedTab = null;
        return true;
    }

    private AnchorRect? Find(string? key)
    {
        if (key != null && anchors.TryGetValue(key, out var a) && a.Frame >= ImGui.GetFrameCount() - 2)
            return a;
        return null;
    }

    private static string WindowKey(TourWindow w) => w == TourWindow.Config ? "config.window" : "main.window";

    // ---------------------------------------------------------------- tours

    public void StartTour(string id)
    {
        var t = TourContent.Tours.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (t == null)
            return;
        tip = null;
        tour = t;
        step = 0;
        stepEntered = false;
        focusBubble = true;
        // Starting a tour answers its "want a tour?" offer.
        Config.SeenTips.Add(t.Id == TourContent.MainTourId ? TourContent.OfferMainTour.Id : TourContent.OfferSettingsTour.Id);
        plugin.SaveConfig();
    }

    /// <summary>Ends the tour; skipped or finished, it is not offered again.</summary>
    public void EndTour()
    {
        if (tour == null)
            return;
        Config.CompletedTours.Add(tour.Id);
        plugin.SaveConfig();
        tour = null;
        requestedTab = null;
    }

    private void GoTo(int index)
    {
        step = Math.Clamp(index, 0, tour!.Steps.Count - 1);
        stepEntered = false;
    }

    private void EnterStep(TourStep s)
    {
        stepEntered = true;
        if (s.Window != TourWindow.None && !plugin.IsWindowOpen(s.Window))
            plugin.OpenWindow(s.Window);
        requestedTab = s.ConfigTab;
    }

    // ---------------------------------------------------------------- tips

    /// <summary>Queues a one-time tip (thread-safe). Ignored if already seen or tips are off.</summary>
    public void ShowTip(Tip t)
    {
        if (queuedTips.TryAdd(t.Id, 0))
            pendingTips.Enqueue(t);
    }

    public void SetTipsEnabled(bool enabled)
    {
        Config.TipsEnabled = enabled;
        if (!enabled)
            tip = null;
        plugin.SaveConfig();
    }

    /// <summary>Every tip and tour offer will be shown again.</summary>
    public void ResetTips()
    {
        Config.SeenTips.Clear();
        Config.CompletedTours.Clear();
        Config.TipsEnabled = true;
        queuedTips.Clear();
        plugin.SaveConfig();
    }

    private void DismissTip()
    {
        if (tip == null)
            return;
        Config.SeenTips.Add(tip.Id);
        plugin.SaveConfig();
        tip = null;
    }

    /// <summary>Tips wait while setting up, in combat or during a tour.</summary>
    private bool CanShowTips => Config.TipsEnabled && !plugin.WizardOpen && !plugin.InCombat;

    private void PickNextTip()
    {
        if (tip != null || !CanShowTips)
            return;
        while (pendingTips.TryDequeue(out var t))
        {
            queuedTips.TryRemove(t.Id, out _);
            if (Config.SeenTips.Contains(t.Id))
                continue;
            if (t.OfferTour != null && Config.CompletedTours.Contains(t.OfferTour))
                continue;
            // Tips about a window only make sense while it is open (try again next time it opens).
            if (t.Window != TourWindow.None && !plugin.IsWindowOpen(t.Window))
                continue;
            tip = t;
            focusBubble = true;
            return;
        }
    }

    // ---------------------------------------------------------------- drawing

    /// <summary>Draws the current tour bubble or tip. Must run after the plugin's windows.</summary>
    public void Draw()
    {
        if (tour != null)
        {
            DrawTourStep();
            return;
        }
        PickNextTip();
        if (tip != null)
        {
            if (!CanShowTips)
                return; // e.g. combat started: keep it for later
            DrawTip(tip);
        }
    }

    private void DrawTourStep()
    {
        var s = tour!.Steps[step];
        if (!stepEntered)
            EnterStep(s);

        var windowOpen = s.Window == TourWindow.None || plugin.IsWindowOpen(s.Window);
        var target = windowOpen ? Find(s.Anchor) ?? (s.Window == TourWindow.None ? null : Find(WindowKey(s.Window))) : null;
        if (target is { } t)
            DrawHighlight(t);

        if (!Begin($"{tour.Name} tour###LBTourBubble", target))
            return;

        Ui.MutedText($"Guided tour: {tour.Name}   {step + 1} / {tour.Steps.Count}");
        using (ImRaii.PushColor(ImGuiCol.PlotHistogram, Accent))
            ImGui.ProgressBar((step + 1f) / tour.Steps.Count, new Vector2(-1, Ui.S(4)), string.Empty);
        ImGui.Spacing();
        ImGui.TextColored(Accent, s.Title);
        ImGui.Spacing();
        ImGui.TextWrapped(s.Body);

        if (s.TryIt != null)
        {
            ImGui.Spacing();
            if (s.Done?.Invoke(plugin) == true)
                ImGui.TextColored(Good, "Done! Press Next.");
            else
                ImGui.TextColored(Ui.Incoming, "Try it: " + s.TryIt);
        }

        if (!windowOpen)
        {
            ImGui.Spacing();
            ImGui.TextColored(Ui.Warning, "The window for this step was closed.");
            ImGui.SameLine();
            if (ImGui.SmallButton("Open it again"))
                plugin.OpenWindow(s.Window);
        }

        ImGui.Spacing();
        ImGui.Separator();
        var last = step == tour.Steps.Count - 1;
        using (ImRaii.Disabled(step == 0))
        {
            if (ImGui.Button("< Back"))
                GoTo(step - 1);
        }
        ImGui.SameLine();
        if (!last)
        {
            using (ImRaii.PushColor(ImGuiCol.Button, Accent with { W = 0.55f }))
            {
                if (ImGui.Button("Next >"))
                    GoTo(step + 1);
            }
        }
        else if (tour.Id == TourContent.MainTourId)
        {
            using (ImRaii.PushColor(ImGuiCol.Button, Accent with { W = 0.55f }))
            {
                if (ImGui.Button("Show me the settings"))
                {
                    EndTour();
                    StartTour(TourContent.SettingsTourId);
                }
            }
            ImGui.SameLine();
            if (ImGui.Button("Finish"))
                EndTour();
        }
        else
        {
            using (ImRaii.PushColor(ImGuiCol.Button, Accent with { W = 0.55f }))
            {
                if (ImGui.Button("Finish"))
                    EndTour();
            }
        }

        if (!last)
        {
            const string skip = "Skip tour";
            ImGui.SameLine(ImGui.GetWindowContentRegionMax().X - ImGui.CalcTextSize(skip).X - ImGui.GetStyle().FramePadding.X * 2);
            if (ImGui.Button(skip))
                EndTour();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Close the tour. Restart it any time with /lb tour or Settings > Help.");
        }
        End();
    }

    private void DrawTip(Tip t)
    {
        var target = Find(t.Anchor) ?? (t.Window == TourWindow.None ? null : Find(WindowKey(t.Window)));
        if (t.Anchor != null && target is { } a)
            DrawHighlight(a);

        if (!Begin("Tip###LBTip", target))
            return;

        Ui.MutedText("LinguaBridge tip");
        ImGui.TextColored(Accent, t.Title);
        ImGui.Spacing();
        ImGui.TextWrapped(t.Body);
        ImGui.Spacing();
        ImGui.Separator();

        if (t.OfferTour != null)
        {
            using (ImRaii.PushColor(ImGuiCol.Button, Accent with { W = 0.55f }))
            {
                if (ImGui.Button("Start the tour"))
                {
                    var id = t.OfferTour;
                    DismissTip();
                    StartTour(id);
                }
            }
            ImGui.SameLine();
            if (ImGui.Button("Not now"))
                DismissTip();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("You can start it later with /lb tour or Settings > Help.");
        }
        else
        {
            using (ImRaii.PushColor(ImGuiCol.Button, Accent with { W = 0.55f }))
            {
                if (ImGui.Button("Got it"))
                    DismissTip();
            }
        }

        const string off = "Turn off tips";
        ImGui.SameLine(ImGui.GetWindowContentRegionMax().X - ImGui.CalcTextSize(off).X - ImGui.GetStyle().FramePadding.X * 2);
        if (ImGui.Button(off))
        {
            DismissTip();
            SetTipsEnabled(false);
            Plugin.ChatGui.Print("Tips turned off. Turn them back on in Settings > Help (or /lb tips on).", "LinguaBridge");
        }
        End();
    }

    /// <summary>Opens the bubble window next to the target (or centred), styled as a popup.</summary>
    private bool Begin(string name, AnchorRect? target)
    {
        ImGui.SetNextWindowPos(Place(target), ImGuiCond.Always);
        ImGui.SetNextWindowSizeConstraints(new Vector2(BubbleWidth, 0), new Vector2(BubbleWidth, 2000));
        if (focusBubble)
        {
            ImGui.SetNextWindowFocus();
            focusBubble = false;
        }

        using var style = ImRaii.PushStyle(ImGuiStyleVar.WindowRounding, Ui.S(8))
                                .Push(ImGuiStyleVar.WindowBorderSize, Math.Max(1f, Ui.S(2)))
                                .Push(ImGuiStyleVar.WindowPadding, Ui.S(14, 12));
        using var color = ImRaii.PushColor(ImGuiCol.Border, Accent);
        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
                                       ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings |
                                       ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.AlwaysAutoResize;
        var open = ImGui.Begin(name, flags);
        if (!open)
        {
            ImGui.End();
            return false;
        }
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + BubbleWidth - Ui.S(28));
        return true;
    }

    private void End()
    {
        ImGui.PopTextWrapPos();
        bubbleSize = ImGui.GetWindowSize();
        ImGui.End();
    }

    /// <summary>Beside the target's window if there is room (so nothing is covered), else above/below the element, else centred.</summary>
    private Vector2 Place(AnchorRect? target)
    {
        var vp = ImGui.GetMainViewport();
        var min = vp.WorkPos;
        var max = vp.WorkPos + vp.WorkSize;
        if (target is not { } t)
            return min + (vp.WorkSize - bubbleSize) / 2;

        var win = WindowRectAround(t);
        var y = Math.Clamp(t.Min.Y - 12, min.Y, Math.Max(min.Y, max.Y - bubbleSize.Y));
        if (win.Max.X + Gap + bubbleSize.X <= max.X)
            return new Vector2(win.Max.X + Gap, y);
        if (win.Min.X - Gap - bubbleSize.X >= min.X)
            return new Vector2(win.Min.X - Gap - bubbleSize.X, y);

        var x = Math.Clamp(t.Min.X, min.X, Math.Max(min.X, max.X - bubbleSize.X));
        if (t.Max.Y + Gap + bubbleSize.Y <= max.Y)
            return new Vector2(x, t.Max.Y + Gap);
        if (t.Min.Y - Gap - bubbleSize.Y >= min.Y)
            return new Vector2(x, t.Min.Y - Gap - bubbleSize.Y);
        return min + (vp.WorkSize - bubbleSize) / 2;
    }

    /// <summary>The plugin window that contains the element (so the bubble goes beside it, not on top of it).</summary>
    private (Vector2 Min, Vector2 Max) WindowRectAround(AnchorRect t)
    {
        foreach (var key in new[] { "main.window", "config.window" })
        {
            if (Find(key) is { } w && t.Min.X >= w.Min.X - 1 && t.Max.X <= w.Max.X + 1 &&
                t.Min.Y >= w.Min.Y - 1 && t.Max.Y <= w.Max.Y + 1)
                return (w.Min, w.Max);
        }
        return (t.Min, t.Max);
    }

    private static void DrawHighlight(AnchorRect t)
    {
        var dl = ImGui.GetForegroundDrawList();
        var pulse = 0.55f + 0.45f * MathF.Sin((float)ImGui.GetTime() * 4f);
        var pad = Ui.S(4, 4);
        dl.AddRectFilled(t.Min - pad, t.Max + pad, ImGui.GetColorU32(Accent with { W = 0.10f }), Ui.S(5));
        dl.AddRect(t.Min - pad, t.Max + pad, ImGui.GetColorU32(Accent with { W = pulse }), Ui.S(5), ImDrawFlags.None, Ui.S(3));
    }
}
