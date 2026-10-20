using System.Numerics;

namespace Mercury.Windows.Dock;

/// <summary>The few Daedalus theme colours the ported dock uses, so Mercury's tab matches the others.</summary>
internal static class DockTheme
{
    public static readonly Vector4 AccentGold = new(0.85f, 0.65f, 0.20f, 1f);
    public static readonly Vector4 TextSecondary = new(0.60f, 0.58f, 0.55f, 1f);
    public static readonly Vector4 StatusRed = new(0.85f, 0.25f, 0.20f, 1f);
}
