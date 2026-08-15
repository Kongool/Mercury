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
    private readonly Dictionary<string, long> lastActiveUnix = new();
    private readonly HashSet<string> currentlyActive = new();
    private readonly Dictionary<string, (float X, float Z)> locations = new();

    private static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    /// <summary>Raised the frame a CE first becomes active (spawns or opens registration).</summary>
    public event Action<string>? CeActivated;

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
            }
        }
    }

    /// <summary>Seconds since this CE was last active this session, or null if not seen yet.</summary>
    public long? SecondsSinceLastActive(string name)
        => this.lastActiveUnix.TryGetValue(name, out var t) ? Now - t : null;
}
