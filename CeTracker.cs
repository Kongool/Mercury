using System;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;

namespace Mercury;

/// <summary>
/// The game exposes no respawn countdown for Critical Encounters, so Mercury times
/// them itself: it watches the dynamic-event slots every frame and records when each
/// CE was last active. Tracking is per-session (cleared on leaving the instance),
/// since respawns are per-instance and stale cross-session data would mislead.
/// </summary>
public sealed class CeTracker
{
    // A CE's map area is large; being anywhere within this many yalms of its marker while
    // it is being fought counts as participating in it.
    private const float ParticipationRange = 60f;

    private readonly Dictionary<string, long> lastActiveUnix = new();
    private readonly HashSet<string> currentlyActive = new();
    private readonly Dictionary<string, (float X, float Z)> locations = new();

    // per-active-CE state used to detect a genuine completion (reached 100% while you were
    // present), so the Challenge Log auto-increment doesn't count fights you skipped or lost
    private readonly Dictionary<string, byte> lastProgress = new();
    private readonly HashSet<string> participated = new();

    private static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    /// <summary>Raised the frame a CE first becomes active (spawns or opens registration).</summary>
    public event Action<string>? CeActivated;

    /// <summary>Raised when a CE you took part in ends at 100% (a genuine completion).</summary>
    public event Action<string>? CeCompleted;

    /// <summary>Raised the first time this session a CE's world position is observed.</summary>
    public event Action<string, float, float>? LocationLearned;

    /// <summary>True if this CE is up right now.</summary>
    public bool IsActive(string name) => this.currentlyActive.Contains(name);

    /// <summary>This session's observed world (X, Z) for a CE, if seen live.</summary>
    public bool TryGetLocation(string name, out float x, out float z)
    {
        if (this.locations.TryGetValue(name, out var p))
        {
            (x, z) = p;
            return true;
        }

        x = z = 0f;
        return false;
    }

    public unsafe void Update()
    {
        var container = DynamicEventContainer.GetInstance();
        if (container is null)
        {
            // left the Occult Crescent instance - forget this session's timers
            this.lastActiveUnix.Clear();
            this.currentlyActive.Clear();
            this.locations.Clear();
            this.lastProgress.Clear();
            this.participated.Clear();
            return;
        }

        var now = Now;
        var events = container->Events;
        for (var i = 0; i < events.Length; i++)
        {
            ref var e = ref events[i];
            var name = e.Name.ToString();
            if (string.IsNullOrEmpty(name))
                continue;

            if (e.State != DynamicEventState.Inactive)
            {
                // Learn the CE's world position (once) for the nearest-crystal hint.
                var pos = e.MapMarker.Position;
                if ((pos.X != 0f || pos.Z != 0f) && !this.locations.ContainsKey(name))
                {
                    this.locations[name] = (pos.X, pos.Z);
                    this.LocationLearned?.Invoke(name, pos.X, pos.Z);
                }

                // Track progress and whether you're present, to judge completion on despawn.
                this.lastProgress[name] = e.Progress;
                if (WithinRange(pos.X, pos.Z, ParticipationRange))
                    this.participated.Add(name);

                // Add returns true only on the frame it first becomes active - the moment
                // to alert (covers registration, warmup and battle states alike).
                if (this.currentlyActive.Add(name))
                    this.CeActivated?.Invoke(name);
                this.lastActiveUnix[name] = now; // keep bumping while it is up
            }
            else if (this.currentlyActive.Remove(name))
            {
                // just despawned - anchor the "last up" time here
                this.lastActiveUnix[name] = now;

                // a CE you were in that ended at 100% is a genuine completion
                if (this.participated.Contains(name) &&
                    this.lastProgress.TryGetValue(name, out var prog) && prog >= 100)
                    this.CeCompleted?.Invoke(name);

                this.participated.Remove(name);
                this.lastProgress.Remove(name);
            }
        }
    }

    /// <summary>Seconds since this CE was last active this session, or null if not seen yet.</summary>
    public long? SecondsSinceLastActive(string name)
        => this.lastActiveUnix.TryGetValue(name, out var t) ? Now - t : null;

    // True if the local player is within <paramref name="range"/> yalms of a world (X, Z).
    private static bool WithinRange(float x, float z, float range)
    {
        var player = Service.ObjectTable.LocalPlayer;
        if (player is null)
            return false;
        var dx = player.Position.X - x;
        var dz = player.Position.Z - z;
        return (dx * dx) + (dz * dz) <= range * range;
    }
}
