using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;

namespace Mercury;

/// <summary>
/// Watches the object table for objects whose name matches a term and raises
/// <see cref="EnemyAppeared"/> once when a matching name first appears. Dedup is by NAME
/// (not entity id, which churns for big CE bosses), with a cooldown so a flickering or
/// re-loading object can't spam - it only re-alerts after the name has been gone a while.
/// </summary>
public sealed class ScanAlerter
{
    private const long ReAlertCooldownSec = 60;

    private readonly HashSet<string> presentLastFrame = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> lastAlertUnix = new(StringComparer.OrdinalIgnoreCase);
    private string activeTerm = string.Empty;

    /// <summary>Raised when a matching name first appears. Args: name, world X, world Z.</summary>
    public event Action<string, float, float>? EnemyAppeared;

    /// <summary>Kinds that can be a scannable enemy - mimics can be a chest (Treasure/EventObj), not just BattleNpc.</summary>
    public static bool IsScanCandidate(IGameObject obj) => obj.ObjectKind
        is ObjectKind.BattleNpc or ObjectKind.EventNpc or ObjectKind.Treasure or ObjectKind.EventObj;

    public void Update(string? term)
    {
        if (string.IsNullOrEmpty(term) || term.Length < 2)
        {
            this.presentLastFrame.Clear();
            this.lastAlertUnix.Clear();
            this.activeTerm = string.Empty;
            return;
        }

        // a changed term starts fresh so whatever is loaded alerts once
        if (term != this.activeTerm)
        {
            this.activeTerm = term;
            this.presentLastFrame.Clear();
            this.lastAlertUnix.Clear();
        }

        // names present this frame, and one representative position per name
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var positions = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);
        foreach (var obj in Service.ObjectTable)
        {
            if (!IsScanCandidate(obj))
                continue;

            var name = obj.Name.TextValue;
            if (string.IsNullOrEmpty(name) ||
                name.IndexOf(term, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            if (present.Add(name))
                positions[name] = obj.Position;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach (var name in present)
        {
            // alert only on a fresh appearance, and never more than once per cooldown
            if (this.presentLastFrame.Contains(name))
                continue;
            if (this.lastAlertUnix.TryGetValue(name, out var last) && now - last < ReAlertCooldownSec)
                continue;

            this.lastAlertUnix[name] = now;
            var p = positions[name];
            this.EnemyAppeared?.Invoke(name, p.X, p.Z);
        }

        this.presentLastFrame.Clear();
        this.presentLastFrame.UnionWith(present);
    }
}
