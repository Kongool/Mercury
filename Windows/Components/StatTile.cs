using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

namespace Mercury.Windows.Components;

/// <summary>
/// One metric: dim uppercase label, strong value, dim sub line, accent tick on the left, and an
/// optional progress bar along the bottom. Ported from Hephaestus.
/// </summary>
internal static class StatTile
{
    /// <summary>Draws a row of equal-width tiles across the available width.</summary>
    public static void Row(params (string Label, string Value, string? Sub, float? Fraction)[] tiles)
    {
        if (tiles.Length == 0)
            return;

        var gap = 8f * ImGuiHelpers.GlobalScale;
        var width = (ImGui.GetContentRegionAvail().X - gap * (tiles.Length - 1)) / tiles.Length;
        for (var i = 0; i < tiles.Length; i++)
        {
            if (i > 0)
                ImGui.SameLine(0, gap);
            var t = tiles[i];
            Draw(t.Label, t.Value, t.Sub, width, t.Fraction);
        }
    }

    public static void Draw(string label, string value, string? sub, float width, float? fraction = null)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var height = MercuryTheme.StatTileHeight * scale + (sub is null ? 0f : 14f * scale);
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + new Vector2(width, height);
        var dl = ImGui.GetWindowDrawList();
        var accent = MercuryTheme.Accent;

        dl.AddRectFilled(origin, end, ImGui.GetColorU32(MercuryTheme.CardBgSoft), 6f * scale);
        dl.AddRect(origin, end, ImGui.GetColorU32(MercuryTheme.WithAlpha(MercuryTheme.BorderDim, 0.6f)), 6f * scale);
        dl.AddRectFilled(origin, new Vector2(origin.X + 3f * scale, end.Y), ImGui.GetColorU32(MercuryTheme.WithAlpha(accent, 0.85f)), 2f);

        var padX = 11f * scale;
        var padY = 7f * scale;

        ImGui.SetWindowFontScale(0.80f);
        dl.AddText(new Vector2(origin.X + padX, origin.Y + padY), ImGui.GetColorU32(MercuryTheme.TextDim), label.ToUpperInvariant());
        var labelH = ImGui.CalcTextSize(label).Y;
        ImGui.SetWindowFontScale(1.25f);
        ImGui.SetCursorScreenPos(new Vector2(origin.X + padX, origin.Y + padY + labelH + 3f * scale));
        MercuryTheme.Text(value, MercuryTheme.TextPrimary);
        ImGui.SetWindowFontScale(1f);

        var y = ImGui.GetCursorScreenPos().Y;
        if (fraction is { } f)
        {
            var barW = width - padX * 2f;
            var h = 4f * scale;
            var bo = new Vector2(origin.X + padX, y);
            dl.AddRectFilled(bo, bo + new Vector2(barW, h), ImGui.GetColorU32(MercuryTheme.BorderDim), h * 0.5f);
            if (f > 0f)
                dl.AddRectFilled(bo, bo + new Vector2(barW * System.Math.Clamp(f, 0f, 1f), h), ImGui.GetColorU32(accent), h * 0.5f);
            y += h + 3f * scale;
        }

        if (!string.IsNullOrEmpty(sub))
        {
            ImGui.SetWindowFontScale(0.85f);
            var clipped = sub;
            var maxW = width - padX * 2f;
            while (clipped.Length > 4 && ImGui.CalcTextSize(clipped).X > maxW)
                clipped = clipped[..^2];
            if (clipped.Length != sub.Length)
                clipped = clipped.TrimEnd() + "…";
            dl.AddText(new Vector2(origin.X + padX, y), ImGui.GetColorU32(MercuryTheme.TextDim), clipped);
            ImGui.SetWindowFontScale(1f);
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, height));
        if (sub != null && ImGui.IsItemHovered())
            ImGui.SetTooltip(sub);
    }
}
