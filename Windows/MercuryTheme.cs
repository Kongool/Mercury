using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace Mercury.Windows;

/// <summary>
/// Mercury palette and shared helpers. Ported from Hephaestus (the Argus kit in Daedalus gold), so
/// Mercury matches the user's other plugins. Gold is the identity accent; green / amber / blue / grey
/// are kept for real status only.
/// </summary>
internal static class MercuryTheme
{
    public const float SidebarWidth = 176f;
    public const float StatTileHeight = 58f;

    // identity
    public static readonly Vector4 Accent = new(0.85f, 0.65f, 0.20f, 1.00f);
    public static readonly Vector4 AccentSoft = new(0.92f, 0.80f, 0.45f, 1.00f);

    // status
    public static readonly Vector4 Green = new(0.20f, 0.75f, 0.35f, 1.00f);
    public static readonly Vector4 Amber = new(0.92f, 0.74f, 0.34f, 1.00f);
    public static readonly Vector4 Blue = new(0.40f, 0.68f, 0.98f, 1.00f);
    public static readonly Vector4 Grey = new(0.45f, 0.45f, 0.50f, 1.00f);
    public static readonly Vector4 Rose = new(0.93f, 0.42f, 0.50f, 1.00f);

    // surfaces
    public static readonly Vector4 CardBg = new(0.070f, 0.090f, 0.105f, 0.85f);
    public static readonly Vector4 CardBgSoft = new(0.085f, 0.105f, 0.120f, 0.55f);
    public static readonly Vector4 CardBgHover = new(0.100f, 0.125f, 0.145f, 0.95f);
    public static readonly Vector4 BorderDim = new(0.22f, 0.26f, 0.30f, 1.00f);
    public static readonly Vector4 Hairline = new(1f, 1f, 1f, 0.055f);

    // text
    public static readonly Vector4 TextPrimary = new(0.95f, 0.93f, 0.89f, 1.00f);
    public static readonly Vector4 TextSecondary = new(0.78f, 0.80f, 0.84f, 1.00f);
    public static readonly Vector4 TextDim = new(0.55f, 0.58f, 0.62f, 1.00f);
    public static readonly Vector4 TextMuted = new(0.40f, 0.42f, 0.46f, 1.00f);

    public const float CardRounding = 7f;
    public const float FrameRounding = 5f;

    public static Vector4 WithAlpha(Vector4 c, float a) => c with { W = a };

    public static void VSpace(float pixels)
        => ImGui.Dummy(new Vector2(0, pixels * ImGuiHelpers.GlobalScale));

    public static void Text(string text, Vector4 color)
    {
        using (ImRaii.PushColor(ImGuiCol.Text, color))
            ImGui.TextUnformatted(text);
    }

    /// <summary>Text that wraps at the content edge, for sentences rather than labels.</summary>
    public static void TextWrapped(string text, Vector4 color)
    {
        ImGui.PushTextWrapPos(0f);
        Text(text, color);
        ImGui.PopTextWrapPos();
    }

    public static void TextScaled(string text, Vector4 color, float fontScale)
    {
        ImGui.SetWindowFontScale(fontScale);
        Text(text, color);
        ImGui.SetWindowFontScale(1f);
    }

    /// <summary>Dim uppercase label, e.g. a sidebar group or settings card title.</summary>
    public static void SectionLabel(string label)
    {
        using (ImRaii.PushColor(ImGuiCol.Text, TextDim))
            ImGui.TextUnformatted(label.ToUpperInvariant());
    }

    /// <summary>Gold section header with a hairline running to the right edge (same as Daedalus).</summary>
    public static void SectionHeader(string label)
    {
        ImGui.Spacing();
        ImGui.TextColored(Accent, label);
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var lineY = (min.Y + max.Y) / 2f;
        var lineStart = new Vector2(max.X + 8f, lineY);
        var lineEnd = new Vector2(
            ImGui.GetWindowPos().X + ImGui.GetWindowWidth() - ImGui.GetStyle().WindowPadding.X, lineY);
        if (lineEnd.X > lineStart.X)
            ImGui.GetWindowDrawList().AddLine(lineStart, lineEnd,
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.20f, 0.20f, 0.24f, 1f)), 1f);
        ImGui.Spacing();
    }

    /// <summary>Page title, muted subtitle, then a hairline (Charon's DrawPageHeader).</summary>
    public static void PageHeader(string title, string? subtitle)
    {
        TextScaled(title, TextPrimary, 1.3f);
        if (!string.IsNullOrEmpty(subtitle))
            Text(subtitle, TextDim);

        var y = ImGui.GetCursorScreenPos().Y + 3f * ImGuiHelpers.GlobalScale;
        var x0 = ImGui.GetCursorScreenPos().X;
        var x1 = x0 + ImGui.GetContentRegionAvail().X;
        ImGui.GetWindowDrawList().AddLine(new Vector2(x0, y), new Vector2(x1, y), ImGui.GetColorU32(Hairline), 1f);
        VSpace(9f);
    }

    public static void Tooltip(string text, float wrapWidth = 300f)
    {
        if (!ImGui.IsItemHovered())
            return;

        using var tip = ImRaii.Tooltip();
        ImGui.PushTextWrapPos(wrapWidth * ImGuiHelpers.GlobalScale);
        Text(text, TextSecondary);
        ImGui.PopTextWrapPos();
    }

    /// <summary>A thin progress bar in the accent colour.</summary>
    public static void Bar(float fraction, float width, Vector4? color = null)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var h = 5f * scale;
        var origin = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        var f = Math.Clamp(fraction, 0f, 1f);
        dl.AddRectFilled(origin, origin + new Vector2(width, h), ImGui.GetColorU32(BorderDim), h * 0.5f);
        if (f > 0f)
            dl.AddRectFilled(origin, origin + new Vector2(width * f, h), ImGui.GetColorU32(color ?? Accent), h * 0.5f);
        ImGui.Dummy(new Vector2(width, h));
    }

    public static IDisposable PushWindowStyle()
    {
        var p = ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, FrameRounding);
        p.Push(ImGuiStyleVar.ChildRounding, CardRounding);
        p.Push(ImGuiStyleVar.ItemSpacing, new Vector2(8, 6) * ImGuiHelpers.GlobalScale);
        return p;
    }
}
