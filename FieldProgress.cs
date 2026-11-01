using System;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace Mercury;

/// <summary>
/// The last-seen field-op levels for one character. The game only loads these while you are inside the
/// matching content, so Mercury remembers them for the Progress page.
/// </summary>
[Serializable]
public sealed class CharacterProgress
{
    public int ElementalLevel { get; set; }
    public float ElementalExpFraction { get; set; }
    public uint ElementalTerritory { get; set; }
    public long ElementalSeenUnix { get; set; }

    public int ResistanceRank { get; set; }
    public float MettleFraction { get; set; }
    public uint BozjaTerritory { get; set; }
    public long BozjaSeenUnix { get; set; }

    public int KnowledgeLevel { get; set; }
    public uint OccultTerritory { get; set; }
    public long OccultSeenUnix { get; set; }
}

/// <summary>Reads Elemental Level, Resistance Rank and Knowledge Level while you are in their zone, and saves them per character.</summary>
public sealed class FieldProgress
{
    // PlayerState.GetContentValue keys (see the ClientStructs doc table).
    private const uint KeyElementalLevel = 4;
    private const uint KeyResistanceRank = 5;
    private const uint KeyKnowledgeLevel = 7;

    private readonly Configuration config;
    private DateTime nextPoll;

    public FieldProgress(Configuration config) => this.config = config;

    /// <summary>The logged-in character's remembered progress (empty if none seen yet).</summary>
    public unsafe CharacterProgress Current
    {
        get
        {
            var ps = PlayerState.Instance();
            if (ps == null || ps->ContentId == 0)
                return new CharacterProgress();
            return this.config.Progress.TryGetValue(ps->ContentId, out var p) ? p : new CharacterProgress();
        }
    }

    /// <summary>Live Eureka state (EXP and the elemental wheel), or null outside Eureka.</summary>
    public static unsafe EurekaState? Eureka()
    {
        var s = PublicContentEureka.GetState();
        return s == null ? null : *s;
    }

    /// <summary>Live Mettle (current, needed), or null outside Bozja.</summary>
    public static unsafe (uint Current, uint Needed)? Mettle()
    {
        var s = PublicContentBozja.GetState();
        return s == null ? null : (s->CurrentExperience, s->NeededExperience);
    }

    public unsafe void Update()
    {
        var now = DateTime.UtcNow;
        if (now < this.nextPoll)
            return;
        this.nextPoll = now.AddSeconds(1);

        var ps = PlayerState.Instance();
        if (ps == null || !ps->IsLoaded || ps->ContentId == 0)
            return;

        var zone = FieldZones.Current;
        var op = FieldZones.OpOf(zone);
        if (op == FieldOp.None)
            return;

        if (!this.config.Progress.TryGetValue(ps->ContentId, out var p))
        {
            p = new CharacterProgress();
            this.config.Progress[ps->ContentId] = p;
        }

        var territory = Service.ClientState.TerritoryType;
        var unix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var changed = false;

        switch (op)
        {
            case FieldOp.Eureka:
            {
                var level = (int)ps->GetContentValue(KeyElementalLevel);
                if (level <= 0)
                    break;
                var fraction = p.ElementalExpFraction;
                if (Eureka() is { NeededExperience: > 0 } s)
                    fraction = (float)s.CurrentExperience / s.NeededExperience;
                changed = level != p.ElementalLevel || territory != p.ElementalTerritory || MathF.Abs(fraction - p.ElementalExpFraction) >= 0.01f;
                p.ElementalLevel = level;
                p.ElementalExpFraction = fraction;
                p.ElementalTerritory = territory;
                p.ElementalSeenUnix = unix;
                break;
            }

            case FieldOp.Bozja:
            {
                var rank = (int)ps->GetContentValue(KeyResistanceRank);
                if (rank <= 0)
                    break;
                var fraction = p.MettleFraction;
                if (Mettle() is { Needed: > 0 } m)
                    fraction = (float)m.Current / m.Needed;
                changed = rank != p.ResistanceRank || territory != p.BozjaTerritory || MathF.Abs(fraction - p.MettleFraction) >= 0.01f;
                p.ResistanceRank = rank;
                p.MettleFraction = fraction;
                p.BozjaTerritory = territory;
                p.BozjaSeenUnix = unix;
                break;
            }

            case FieldOp.Occult:
            {
                var level = (int)ps->GetContentValue(KeyKnowledgeLevel);
                if (level <= 0)
                    break;
                changed = level != p.KnowledgeLevel || territory != p.OccultTerritory;
                p.KnowledgeLevel = level;
                p.OccultTerritory = territory;
                p.OccultSeenUnix = unix;
                break;
            }
        }

        // The "seen" time updates every poll but only matters to the minute, so save on real changes only.
        if (changed)
            this.config.Save();
    }
}
