using System;
using System.Collections.Generic;
using Dalamud.Configuration;

namespace Mercury;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    /// <summary>Master switch: fire a prominent alert (toast + chat, with sound) when a watched FATE or CE goes up.</summary>
    public bool FateAlertsEnabled { get; set; } = true;

    /// <summary>Show the always-visible watched-FATE status panel above the tabs.</summary>
    public bool ShowWatchPanel { get; set; } = true;

    /// <summary>Alert on every FATE, not just the watch-list below. Off by default (too noisy).</summary>
    public bool AlertOnAnyFate { get; set; } = false;

    /// <summary>
    /// Encounter names to alert on when they spawn, matched case-insensitively. Shared
    /// across FATEs and Critical Encounters - a name alerts regardless of which system it
    /// belongs to. Seeded with the phantom-job shard CEs (Dark Artistry = Necromancer,
    /// Appalling Behavior = Blue Mage); right-click a FATE or CE row to add or remove one.
    /// </summary>
    public HashSet<string> AlertFates { get; set; } =
        new(StringComparer.OrdinalIgnoreCase) { "Dark Artistry", "Appalling Behavior" };

    /// <summary>Automatically open the window when entering the Occult Crescent, close it when leaving.</summary>
    public bool AutoOpenInOccult { get; set; } = true;

    /// <summary>Show record description text inline (may contain lore spoilers).</summary>
    public bool ShowDescriptions { get; set; } = false;

    // Pot CE respawn tracking (manual "popped" timestamps, unix seconds; 0 = untracked).
    public long PotPoppedNorthUnix;
    public long PotPoppedSouthUnix;
    public int PotRespawnMinutes = 25;

    // Enemy scan alert: chat/toast when an enemy whose name contains ScanAlertName streams in.
    public bool ScanAlertEnabled;
    public string ScanAlertName = string.Empty;

    /// <summary>
    /// Learned world (X, Z) positions of FATEs/CEs by name, so the nearest-crystal hint
    /// survives across sessions. Filled the first time an encounter is seen live (the game
    /// exposes no static spawn coords for these). Stored as [worldX, worldZ].
    /// </summary>
    public Dictionary<string, float[]> EncounterLocations { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Martial Memories objectives the player has manually ticked off, keyed by their
    /// PhantomWeaponExTodoDetailTxt row id. Used as a fallback when live progress can't be
    /// scraped from the in-game window (the game exposes no module for this data).
    /// </summary>
    public HashSet<int> MartialDone { get; set; } = new();

    /// <summary>Overlay live progress read from the open in-game Martial Memories window.</summary>
    public bool MartialUseScraping { get; set; } = true;

    public void Save() => Service.PluginInterface.SavePluginConfig(this);
}
