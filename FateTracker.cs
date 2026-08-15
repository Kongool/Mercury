using System;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;

namespace Mercury;

/// <summary>
/// FATEs, like Critical Encounters, expose no respawn countdown, so Mercury times them
/// itself: while you are inside the Occult Crescent it watches the live FATE list every
/// frame and records when each FATE was last active. Tracking is per-session (cleared on
/// leaving the instance), since respawns are per-instance and stale data would mislead.
///
/// Unlike CE slots - which persist in their container and merely flip to Inactive - a
/// FATE is removed from <see cref="FateManager.Fates"/> entirely once it ends, so we anchor
/// its "last up" time the frame it stops being active or disappears from the list.
/// </summary>
public sealed class FateTracker
{
    private readonly Dictionary<string, long> lastActiveUnix = new();
    private readonly HashSet<string> currentlyActive = new();
    private readonly Dictionary<string, (float X, float Z)> locations = new();

    // reused each frame to avoid per-update allocations
    private readonly HashSet<string> activeThisFrame = new();
    private readonly List<string> justEnded = new();

    private static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    /// <summary>Raised the frame a FATE first becomes active (goes up or respawns).</summary>
    public event Action<string>? FateActivated;

    /// <summary>Raised the first time this session a FATE's world position is observed.</summary>
    public event Action<string, float, float>? LocationLearned;

    /// <summary>This session's observed world (X, Z) for a FATE, if seen live.</summary>
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

    /// <summary>Names the tracker has seen this session (whether or not currently up).</summary>
    public IReadOnlyCollection<string> TrackedNames => this.lastActiveUnix.Keys;

    /// <summary>Seconds since this FATE was last active this session, or null if not seen yet.</summary>
    public long? SecondsSinceLastActive(string name)
        => this.lastActiveUnix.TryGetValue(name, out var t) ? Now - t : null;

    /// <summary>True if this FATE is up right now.</summary>
    public bool IsActive(string name) => this.currentlyActive.Contains(name);

    public unsafe void Update()
    {
        // Only meaningful inside the Occult Crescent; elsewhere FateManager is full of
        // unrelated overworld FATEs, so forget this session's timers when we leave.
        if (PublicContentOccultCrescent.GetInstance() == null)
        {
            this.lastActiveUnix.Clear();
            this.currentlyActive.Clear();
            this.locations.Clear();
            return;
        }

        var manager = FateManager.Instance();
        if (manager == null)
            return;

        var now = Now;
        this.activeThisFrame.Clear();

        ref var fates = ref manager->Fates;
        for (var i = 0; i < fates.Count; i++)
        {
            var fate = fates[i].Value;
            if (fate == null)
                continue;

            var name = fate->Name.ToString();
            if (string.IsNullOrEmpty(name))
                continue;

            // Preparing = about to pop, Running = live; both count as "up".
            if (fate->State is FateState.Preparing or FateState.Running)
            {
                this.activeThisFrame.Add(name);

                // Learn the spawn position (once) before announcing, so the alert can name
                // the nearest crystal. Location is 0,0 until the game places the FATE.
                var loc = fate->Location;
                if ((loc.X != 0f || loc.Z != 0f) && !this.locations.ContainsKey(name))
                {
                    this.locations[name] = (loc.X, loc.Z);
                    this.LocationLearned?.Invoke(name, loc.X, loc.Z);
                }

                // Add returns true only on the frame it first becomes active - the
                // moment to alert. It stays in the set (and re-fires on respawn).
                if (this.currentlyActive.Add(name))
                    this.FateActivated?.Invoke(name);
                this.lastActiveUnix[name] = now; // keep bumping while it is up
            }
        }

        // Anything we were tracking as active that is no longer up (ended, failed, or gone
        // from the list) gets its "last up" anchored here.
        this.justEnded.Clear();
        foreach (var name in this.currentlyActive)
            if (!this.activeThisFrame.Contains(name))
                this.justEnded.Add(name);

        foreach (var name in this.justEnded)
        {
            this.currentlyActive.Remove(name);
            this.lastActiveUnix[name] = now;
        }
    }
}
