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

/// <summary>Writing a message: target language, channel, preview (sent exactly as shown), send or translate-only.</summary>
public sealed partial class MainWindow
{
    private void DrawCompose()
    {
        var cfg = plugin.Configuration;
        var a = Languages.Get(cfg.LanguageA);
        var b = Languages.Get(cfg.LanguageB);
        ImGui.Dummy(Ui.S(0, 4));

        if (replyPrefix != null)
        {
            using (ImRaii.Group())
            {
                Ui.Icon(FontAwesomeIcon.Reply, Ui.Accent);
                ImGui.SameLine();
                ImGui.AlignTextToFramePadding();
                ImGui.TextColored(Ui.Accent, LF("Replying to {0}", replyLabel));
                ImGui.SameLine();
                if (Ui.GhostIconButton("cancelreply", FontAwesomeIcon.Times, L("Cancel the reply (back to the channel list)"), Ui.Danger))
                    replyPrefix = null;
            }
        }
        else
        {
            var mode = cfg.ComposeTranslateOnly ? 1 : 0;
            if (Ui.Segmented("mode", new[] { L("Send to chat"), L("Translate only") }, ref mode, new[]
                {
                    L("What you type is translated, then sent to the game chat."),
                    L("Nothing is sent: the translation is copied so you can paste it anywhere\n" +
                    "(Aetherphone's ChocoChat or Velvet, Discord, a website...).\n" +
                    "Also: /lb clip translates what you copied."),
                }))
            {
                cfg.ComposeTranslateOnly = mode == 1;
                plugin.SaveConfig();
                if (mode == 1)
                    plugin.Tutorial.ShowTip(TourContent.FirstTranslateOnly);
            }
            plugin.Tutorial.Anchor("main.mode");
            ImGui.SameLine(0, Ui.S(10));
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(Ui.Muted, cfg.ComposeTranslateOnly ? L("Enter: translate and copy") : L("Enter: translate and send"));
        }

        if (focusInput)
        {
            ImGui.SetKeyboardFocusHere();
            focusInput = false;
        }
        bool submitted;
        if (cfg.RoleplayMode)
        {
            // Bigger box for RP posts; long translations are split into several chat lines automatically.
            submitted = ImGui.InputTextMultiline("##input", ref input, 2000,
                                                 new Vector2(-1, ImGui.GetTextLineHeight() * 4.5f),
                                                 ImGuiInputTextFlags.EnterReturnsTrue);
            plugin.Tutorial.Anchor("main.input");
            var bytes = System.Text.Encoding.UTF8.GetByteCount(input);
            ImGui.TextColored(bytes > 500 ? Ui.Warning : Ui.Muted,
                              bytes > 500 ? LF("{0} bytes: longer than one chat line (500), it will be split", bytes)
                                          : LF("{0} / 500 bytes per chat line", bytes));
        }
        else
        {
            ImGui.SetNextItemWidth(-1);
            submitted = ImGui.InputTextWithHint("##input", L("Type your message in any language..."), ref input, 400,
                                                ImGuiInputTextFlags.EnterReturnsTrue);
            plugin.Tutorial.Anchor("main.input");
        }
        var translateOnly = cfg.ComposeTranslateOnly && replyPrefix == null;
        if (submitted && !busy)
        {
            _ = translateOnly ? TranslateAndCopy() : TranslateAndSend();
            focusInput = true;
        }

        // Options on the left, actions on the right.
        // Target: auto, the pair first, then every other language (to write to someone outside the pair).
        var codes = new[] { (string?)null, a.Code, b.Code }
                    .Concat(Languages.All.Where(l => l.Code != a.Code && l.Code != b.Code).Select(l => (string?)l.Code))
                    .ToArray();
        var targets = codes.Select(c => c == null ? L("Auto (other language)") : LF("To {0}", L(Languages.Get(c).Name))).ToArray();
        var t = Math.Max(0, Array.IndexOf(codes, targetCode));
        Ui.Icon(FontAwesomeIcon.Language, Ui.Muted);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(Ui.S(165));
        if (ImGui.Combo("##target", ref t, targets, targets.Length))
            targetCode = codes[t];
        Ui.Tooltip(L("Language to translate into"));
        plugin.Tutorial.Anchor("main.target");

        var empty = string.IsNullOrWhiteSpace(input);
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        if (translateOnly)
        {
            var w = Ui.IconTextButtonWidth(FontAwesomeIcon.Paste, L("Paste & translate")) +
                    Ui.IconTextButtonWidth(FontAwesomeIcon.Copy, L("Translate & copy")) + spacing;
            Ui.AlignRight(w);
            using (ImRaii.Disabled(busy))
            {
                if (Ui.Button(FontAwesomeIcon.Paste, L("Paste & translate"),
                              tooltip: L("Translates what you copied (Ctrl+C) and copies the translation back.")))
                {
                    var clip = Clipboard.GetText();
                    if (string.IsNullOrWhiteSpace(clip))
                    {
                        SetStatus(L("The clipboard has no text."), StatusKind.Warning);
                    }
                    else
                    {
                        input = clip.Trim();
                        _ = TranslateAndCopy();
                    }
                }
            }
            ImGui.SameLine();
            using (ImRaii.Disabled(busy || empty))
            {
                if (Ui.Button(FontAwesomeIcon.Copy, L("Translate & copy"), primary: true,
                              tooltip: empty ? L("Type something first.") : null))
                    _ = TranslateAndCopy();
            }
            plugin.Tutorial.Anchor("main.preview");
            DrawPreview(canCheckMeaning: false);
            return;
        }

        ImGui.SameLine(0, Ui.S(10));
        using (ImRaii.Disabled(replyPrefix != null))
        {
            Ui.Icon(FontAwesomeIcon.Comments, Ui.Muted);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(Ui.S(160));
            var channelLabels = Array.ConvertAll(ChannelLabels, L);
            ImGui.Combo("##channel", ref channelIndex, channelLabels, channelLabels.Length);
        }
        Ui.Tooltip(replyPrefix != null ? L("Replying: the message goes where the original came from.") : L("Chat channel to send to"));
        plugin.Tutorial.Anchor("main.channel");

        var needsName = replyPrefix == null && SendChannels[channelIndex].Prefix == TellToName;
        if (needsName)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(Ui.S(170));
            ImGui.InputTextWithHint("##tellname", L("First Last@World"), ref tellName, 64);
            Ui.Tooltip(L("Who receives the tell: First Last@World (the world can be left out on your own world)."));
        }

        var noName = needsName && tellName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < 2;
        var buttons = Ui.IconTextButtonWidth(FontAwesomeIcon.Eye, L("Preview")) +
                      Ui.IconTextButtonWidth(FontAwesomeIcon.PaperPlane, L("Send")) + spacing;
        Ui.AlignRight(buttons);
        using (ImRaii.Disabled(busy || empty || noName))
        {
            if (Ui.Button(FontAwesomeIcon.Eye, L("Preview"),
                          tooltip: noName ? L("Type the name of the player first (First Last@World).")
                                          : L("Translate without sending, to check it first. Send then sends exactly this.")))
                _ = Preview();
            plugin.Tutorial.Anchor("main.preview");
            ImGui.SameLine();
            if (Ui.Button(FontAwesomeIcon.PaperPlane, L("Send"), primary: true,
                          tooltip: empty ? L("Type something first.")
                                 : noName ? L("Type the name of the player first (First Last@World).")
                                 : L("Translate and send (Enter)")))
                _ = TranslateAndSend();
        }

        DrawPreview(canCheckMeaning: true);
    }

    private void DrawPreview(bool canCheckMeaning)
    {
        if (preview.Length > 0)
        {
            ImGui.Dummy(Ui.S(0, 2));
            Ui.Callout(FontAwesomeIcon.Eye, Ui.Accent, null, () =>
            {
                ImGui.TextWrapped(preview);
                if (backTranslation.Length > 0)
                {
                    using (ImRaii.PushColor(ImGuiCol.Text, Ui.Muted))
                        ImGui.TextWrapped(LF("Means: {0}", backTranslation));
                    Ui.Tooltip(L("Back-translation into your language, to check the meaning."));
                }
                if (ImGui.SmallButton(L("Copy") + "##preview"))
                {
                    ImGui.SetClipboardText(preview);
                    SetStatus(L("Translation copied to the clipboard."), StatusKind.Success);
                }
                if (canCheckMeaning && backTranslation.Length == 0 && draft?.Result != null)
                {
                    ImGui.SameLine();
                    using (ImRaii.Disabled(checkingMeaning))
                    {
                        if (ImGui.SmallButton(L("Check the meaning") + "##preview"))
                            _ = CheckMeaning();
                    }
                    Ui.Tooltip(L("Translates it back into your language (one more request)."));
                }
                ImGui.SameLine();
                if (ImGui.SmallButton(L("Dismiss") + "##preview"))
                {
                    preview = string.Empty;
                    backTranslation = string.Empty;
                    draft = null;
                }
            });
        }
        if (status.Length > 0)
        {
            ImGui.Dummy(Ui.S(0, 1));
            Ui.Status(status, statusKind);
        }
    }

    private Language? SelectedTarget() => targetCode == null ? null : Languages.TryGet(targetCode);

    /// <summary>Chat prefix the message goes to: the reply target, the tell name, or the selected channel.</summary>
    private string CurrentPrefix()
    {
        if (replyPrefix != null)
            return replyPrefix;
        var prefix = SendChannels[channelIndex].Prefix;
        return prefix == TellToName ? $"/t {tellName.Trim()} " : prefix;
    }

    private async Task Preview()
    {
        busy = true;
        SetStatus(L("Translating..."), StatusKind.Busy);
        backTranslation = string.Empty;
        draft = null;
        try
        {
            var d = await plugin.PrepareOutgoingAsync(input.Trim(), SelectedTarget(), CurrentPrefix());
            if (d.Result == null)
            {
                preview = string.Empty;
                SetStatus(L("Already in the target language: nothing to translate."), StatusKind.Info);
                return;
            }
            draft = d;
            preview = d.Body;
            SetStatus($"{L(d.Result.Source.Name)} > {L(d.Result.Target.Name)} ({d.Result.Engine})", StatusKind.Info); // loc-ok: codes / numbers only
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, StatusKind.Error);
        }
        finally
        {
            busy = false;
            previewed = true;
        }
    }

    /// <summary>Back-translation of the preview, only when asked (it costs one more request).</summary>
    private async Task CheckMeaning()
    {
        var d = draft;
        if (d?.Result == null || Languages.TryGet(d.Result.Source.Code) is not { } src)
            return;
        checkingMeaning = true;
        try
        {
            var back = await plugin.Translator.TranslateAsync(d.Body, src);
            backTranslation = back?.Text ?? d.Original;
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, StatusKind.Error);
        }
        finally
        {
            checkingMeaning = false;
        }
    }

    private async Task TranslateSkipped(HistoryEntry e)
    {
        busy = true;
        SetStatus(L("Translating..."), StatusKind.Busy);
        try
        {
            SetStatus(await plugin.TranslateSkippedAsync(e)
                          ? L("Translated.")
                          : L("Already in the target language: nothing to translate."),
                      StatusKind.Info);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, StatusKind.Error);
        }
        finally
        {
            busy = false;
        }
    }

    /// <summary>Translate-only mode: translates the box and copies the result; nothing is sent.</summary>
    private async Task TranslateAndCopy()
    {
        var text = input.Trim();
        if (text.Length == 0)
            return;
        busy = true;
        SetStatus(L("Translating..."), StatusKind.Busy);
        backTranslation = string.Empty;
        draft = null;
        try
        {
            var r = await plugin.TranslateTextAsync(text, SelectedTarget());
            if (r == null)
            {
                preview = string.Empty;
                SetStatus(L("Already in the target language: nothing to translate."), StatusKind.Info);
                return;
            }
            preview = r.Text;
            plugin.CopyToClipboard(r.Text);
            SetStatus(LF("{0} > {1} ({2}), copied to the clipboard.", L(r.Source.Name), L(r.Target.Name), r.Engine), StatusKind.Success);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, StatusKind.Error);
        }
        finally
        {
            busy = false;
        }
    }

    private async Task TranslateAndSend()
    {
        var text = input.Trim();
        if (text.Length == 0)
            return;
        busy = true;
        SetStatus(L("Translating..."), StatusKind.Busy);
        try
        {
            var prefix = CurrentPrefix();
            var target = SelectedTarget();
            // The previewed translation is sent as it is, as long as the text, language and channel did not change.
            var d = draft;
            var sent = d != null && d.Original == text && d.Prefix == prefix && d.Target?.Code == target?.Code
                ? await plugin.SendDraftAsync(d)
                : await plugin.TranslateAndSendAsync(text, target, prefix);
            if (sent)
            {
                input = string.Empty;
                preview = string.Empty;
                backTranslation = string.Empty;
                draft = null;
                SetStatus(L("Sent."), StatusKind.Success);
            }
            else
            {
                SetStatus(L("Could not send. See the chat log for details."), StatusKind.Error);
            }
        }
        finally
        {
            busy = false;
        }
    }
}
