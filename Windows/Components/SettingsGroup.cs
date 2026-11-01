using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace Mercury.Windows.Components;

/// <summary>
/// A titled card of settings rows: label left, control right, hairlines between rows, hover help.
/// The card background is drawn after the content so it sizes to fit. Ported from Hephaestus.
/// </summary>
internal sealed class SettingsGroup : IDisposable
{
    private const float PaddingX = 12f;
    private const float PaddingY = 6f;
    private const float GroupGap = 14f;
    private const float RowHeight = 36f;

    private readonly Vector2 origin;
    private readonly float width;
    private bool firstRow = true;

    public static SettingsGroup Begin(string title)
    {
        if (title.Length > 0)
        {
            MercuryTheme.SectionLabel(title);
            MercuryTheme.VSpace(2f);
        }

        return new SettingsGroup();
    }

    private SettingsGroup()
    {
        var scale = ImGuiHelpers.GlobalScale;
        this.origin = ImGui.GetCursorScreenPos();
        this.width = ImGui.GetContentRegionAvail().X;

        var dl = ImGui.GetWindowDrawList();
        dl.ChannelsSplit(2);
        dl.ChannelsSetCurrent(1);

        ImGui.SetCursorScreenPos(this.origin + new Vector2(PaddingX, PaddingY) * scale);
        ImGui.BeginGroup();
    }

    private float RightEdge => this.origin.X + this.width - PaddingX * ImGuiHelpers.GlobalScale;

    public void Dispose()
    {
        ImGui.EndGroup();
        var scale = ImGuiHelpers.GlobalScale;
        var end = new Vector2(this.origin.X + this.width, ImGui.GetItemRectMax().Y + PaddingY * scale);

        var dl = ImGui.GetWindowDrawList();
        dl.ChannelsSetCurrent(0);
        var rounding = MercuryTheme.CardRounding * scale;
        dl.AddRectFilled(this.origin, end, ImGui.GetColorU32(MercuryTheme.CardBgSoft), rounding);
        dl.AddRect(this.origin, end, ImGui.GetColorU32(MercuryTheme.WithAlpha(MercuryTheme.BorderDim, 0.55f)), rounding);
        dl.ChannelsMerge();

        ImGui.SetCursorScreenPos(new Vector2(this.origin.X, end.Y));
        ImGui.Dummy(new Vector2(this.width, 0f));
        MercuryTheme.VSpace(GroupGap);
    }

    /// <summary>A row with a right-aligned control of the given (unscaled) width.</summary>
    public void Row(string label, string? help, float controlWidth, Action drawControl, float controlHeight = 0f)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var rowOrigin = ImGui.GetCursorScreenPos();
        var rowH = RowHeight * scale;
        var right = this.RightEdge;
        var midY = rowOrigin.Y + rowH * 0.5f;
        var hovered = ImGui.IsMouseHoveringRect(rowOrigin, new Vector2(right, rowOrigin.Y + rowH));

        if (!this.firstRow)
            ImGui.GetWindowDrawList().AddLine(rowOrigin, rowOrigin with { X = right }, ImGui.GetColorU32(MercuryTheme.Hairline), 1f);
        this.firstRow = false;

        var labelSize = ImGui.CalcTextSize(label);
        ImGui.SetCursorScreenPos(new Vector2(rowOrigin.X, midY - labelSize.Y * 0.5f));
        MercuryTheme.Text(label, hovered ? MercuryTheme.TextPrimary : MercuryTheme.TextSecondary);
        var labelHovered = ImGui.IsItemHovered();

        var iconHovered = false;
        if (!string.IsNullOrEmpty(help) && hovered)
        {
            var iconStr = FontAwesomeIcon.InfoCircle.ToIconString();
            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                var iconSize = ImGui.CalcTextSize(iconStr);
                ImGui.SetCursorScreenPos(new Vector2(rowOrigin.X + labelSize.X + 7f * scale, midY - iconSize.Y * 0.5f));
                MercuryTheme.Text(iconStr, MercuryTheme.WithAlpha(MercuryTheme.TextMuted, 0.9f));
            }

            iconHovered = ImGui.IsItemHovered();
        }

        if (!string.IsNullOrEmpty(help) && (labelHovered || iconHovered))
        {
            using var tip = ImRaii.Tooltip();
            ImGui.PushTextWrapPos(300f * scale);
            MercuryTheme.Text(help, MercuryTheme.TextSecondary);
            ImGui.PopTextWrapPos();
        }

        var h = controlHeight > 0f ? controlHeight * scale : ImGui.GetFrameHeight();
        ImGui.SetCursorScreenPos(new Vector2(right - controlWidth * scale, midY - h * 0.5f));
        drawControl();

        ImGui.SetCursorScreenPos(rowOrigin);
        ImGui.Dummy(new Vector2(right - rowOrigin.X, rowH));
    }

    public bool Toggle(string label, string? help, ref bool value)
    {
        var changed = false;
        var v = value;
        this.Row(label, help, 38f, () => changed = ToggleSwitch.Draw($"##{label}", ref v), ToggleSwitch.Height);
        value = v;
        return changed;
    }
}
