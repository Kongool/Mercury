using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;

namespace Mercury;

/// <summary>
/// Watches the object table every frame for objects whose name matches a term and raises
/// <see cref="EnemyAppeared"/> once per object - both for ones already present when the
/// term is set and for new ones that stream in. Despawned objects are forgotten so they
/// re-alert if they come back.
/// </summary>
public sealed class ScanAlerter
{
    private readonly HashSet<uint> seen = new();
    private string activeTerm = string.Empty;

    /// <summary>Raised when a matching object first appears. Args: name, world X, world Z.</summary>
    public event Action<string, float, float>? EnemyAppeared;

    /// <summary>Kinds that can be a scannable enemy - mimics can be a chest (Treasure/EventObj), not just BattleNpc.</summary>
    public static bool IsScanCandidate(IGameObject obj) => obj.ObjectKind
        is ObjectKind.BattleNpc or ObjectKind.EventNpc or ObjectKind.Treasure or ObjectKind.EventObj;

    public void Update(string? term)
    {
        if (string.IsNullOrEmpty(term) || term.Length < 2)
        {
            this.seen.Clear();
            this.activeTerm = string.Empty;
            return;
        }

        // a changed term starts fresh, so whatever is already loaded alerts once
        if (term != this.activeTerm)
        {
            this.activeTerm = term;
            this.seen.Clear();
        }

        var current = new HashSet<uint>();
        foreach (var obj in Service.ObjectTable)
        {
            if (!IsScanCandidate(obj))
                continue;

            var name = obj.Name.TextValue;
            if (string.IsNullOrEmpty(name) ||
                name.IndexOf(term, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            var id = obj.EntityId;
            current.Add(id);

            // Add returns true only the first time we see this entity for this term
            if (this.seen.Add(id))
                this.EnemyAppeared?.Invoke(name, obj.Position.X, obj.Position.Z);
        }

        // forget entities that despawned so they re-alert if they return
        this.seen.IntersectWith(current);
    }
}
