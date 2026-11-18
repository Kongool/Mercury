using System.Collections.Generic;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.Text.SeStringHandling;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Achievement = FFXIVClientStructs.FFXIV.Client.Game.UI.Achievement;
using MapSheet = Lumina.Excel.Sheets.Map;

namespace Mercury;

/// <summary>A place on a zone map, in the map coordinates the game shows.</summary>
public sealed record StoryMark(string Name, float X, float Y);

/// <summary>
/// One Krile story step inside a Eureka zone. <paramref name="Flag"/> is where the map flag goes (the spot
/// you wouldn't otherwise know); null when there's nowhere useful to flag.
/// </summary>
public sealed record EurekaStoryStep(int Id, FieldZone Zone, int Level, string Todo, string Unlocks, StoryMark? Flag);

/// <summary>
/// The story steps inside the four Eureka zones. They aren't quests (no journal entry, no map markers) and the
/// game exposes no per-step progress, so this is a hand-written table from the FFXIV Console Games Wiki zone
/// pages. Flag spots (Krile, Gerolt and the story objects) come from the game's Level sheet. Ids are in story order.
/// </summary>
public static class EurekaStory
{
    private static readonly StoryMark KrileAnemos = new("Krile", 18.5f, 31.9f);
    private static readonly StoryMark KrilePagos = new("Krile", 3.7f, 24.3f);
    private static readonly StoryMark KrilePyros = new("Krile", 16.6f, 24.9f);
    private static readonly StoryMark KrileHydatos = new("Krile", 20.5f, 13.7f);

    public static readonly IReadOnlyList<EurekaStoryStep> Steps = new[]
    {
        // Anemos
        new EurekaStoryStep(1, FieldZone.Anemos, 1, "Talk to Krile, then Gerolt. Bring Krile a Protean Crystal from any monster.", "Gear upgrades, Magia Board", new("Gerolt", 18.0f, 32.3f)),
        new EurekaStoryStep(2, FieldZone.Anemos, 3, "Talk to Krile, then the Confluence in the Observatory. Go in daytime; monsters spawn inside at night.", "2nd magicite slot", new("Confluence · Observatory", 13.6f, 20.3f)),
        new EurekaStoryStep(3, FieldZone.Anemos, 5, "Talk to Krile, then the Confluence below the big cliff at the Orchard.", "3rd magicite slot", new("Confluence · The Orchard", 24.5f, 23.1f)),
        new EurekaStoryStep(4, FieldZone.Anemos, 13, "Talk to Krile, then the Confluence at Val River Swale.", "4th magicite slot", new("Confluence · Val River Swale", 21.0f, 13.0f)),
        new EurekaStoryStep(5, FieldZone.Anemos, 17, "Talk to Krile, then the Confluence at Uncanny Valley (32.8, 18.4), then the Aethernet Relay at Aethernet Control with 99 Anemos Crystals.", "5th magicite slot, Eureka Pagos", new("Confluence · Uncanny Valley", 32.8f, 18.4f)),

        // Pagos
        new EurekaStoryStep(6, FieldZone.Pagos, 20, "Talk to Krile when you arrive.", "", KrilePagos),
        new EurekaStoryStep(7, FieldZone.Pagos, 21, "Talk to Krile, then the Depleted Aetherial Stream on the upper hill at the Cones.", "Vlondette's Retreat aetheryte", new("Aetherial Stream · The Cones", 16.5f, 28.9f)),
        new EurekaStoryStep(8, FieldZone.Pagos, 23, "Talk to Krile, then the Depleted Aetherial Stream at Val River Belly.", "Geothermal Studies aetheryte", new("Aetherial Stream · Val River Belly", 26.4f, 30.6f)),
        new EurekaStoryStep(9, FieldZone.Pagos, 25, "Talk to Krile, then the Magicite at the Eastern Edge and inspect the spot next to it (30.7, 21.0).", "Gravitational Studies aetheryte, Pagos weapons", new("Magicite · Eastern Edge", 30.7f, 21.1f)),
        new EurekaStoryStep(10, FieldZone.Pagos, 29, "Talk to Krile, then drop off the cliff at Hot Water and talk to Ejika Tsunjika.", "", new("Ejika · Western Edge", 10.1f, 21.4f)),
        new EurekaStoryStep(11, FieldZone.Pagos, 35, "Talk to Krile, then the aether column at the Fumarole.", "Eureka Pyros", new("Aether column · The Fumarole", 21.9f, 12.5f)),

        // Pyros
        new EurekaStoryStep(12, FieldZone.Pyros, 35, "Talk to Krile, then again at the Living Foundry. Back at camp talk to Gerolt and Drake, make the Paralyze L mneme at the Logos Manipulator, then the Confluence at the Northeastern Ice Needles.", "Logos actions", new("Confluence · NE Ice Needles", 27.8f, 27.2f)),
        new EurekaStoryStep(13, FieldZone.Pyros, 38, "Talk to Krile and Drake, then the Promising Scrap at Surgate Town House, back to Drake and Krile, then the Confluence at the Firing Chamber (23.8, 17.0).", "Two logos actions at once", new("Promising Scrap · Surgate Town House", 18.7f, 30.7f)),
        new EurekaStoryStep(14, FieldZone.Pyros, 40, "Talk to Krile, then the Confluence at West Flamerock.", "", new("Confluence · West Flamerock", 17.2f, 11.7f)),
        new EurekaStoryStep(15, FieldZone.Pyros, 45, "Talk to Krile, then talk to her again at the Living Foundry.", "", new("Krile · Living Foundry", 13.8f, 20.9f)),
        new EurekaStoryStep(16, FieldZone.Pyros, 50, "Talk to Krile, then the Confluence at East Flamerock, back to Krile, then go to the Cavern of the Second Cant (14.5, 37.6).", "Eureka Hydatos", new("Confluence · East Flamerock", 34.1f, 7.3f)),

        // Hydatos
        new EurekaStoryStep(17, FieldZone.Hydatos, 50, "Talk to Krile, then Gerolt, then Krile again.", "Hydatos weapons", new("Gerolt", 21.4f, 13.6f)),
        new EurekaStoryStep(18, FieldZone.Hydatos, 51, "Talk to Krile, meet her at the Central Columns, then the Pooled Aether at Val River Source (25.7, 30.7), and back to her.", "", new("Krile · Central Columns", 20.4f, 24.8f)),
        new EurekaStoryStep(19, FieldZone.Hydatos, 54, "Talk to Krile, meet her at the Western Columns, then the Pooled Aether on the tall stone pillar at West Val River Bank (6.5, 15.7), and back to her.", "", new("Krile · Western Columns", 10.7f, 29.7f)),
        new EurekaStoryStep(20, FieldZone.Hydatos, 57, "Talk to Krile, meet her at the Eastern Columns, then the Pooled Aether at East Val River Bank (30.8, 14.4), and back to her.", "", new("Krile · Eastern Columns", 31.3f, 27.3f)),
        new EurekaStoryStep(21, FieldZone.Hydatos, 60, "Talk to Krile at the Aetherbridge Foundation, follow her into the Board Room, read the Scattered Tomes, then pick Ejika or Krile (rewards are the same).", "The Baldesion Arsenal (via the Expedition Scholar at camp)", new("Krile · Aetherbridge Foundation", 24.1f, 30.7f)),
        new EurekaStoryStep(22, FieldZone.Hydatos, 60, "Clear the Baldesion Arsenal, then talk to Krile for the true ending.", "True ending", null),
    };

    // Proof that a whole zone's story is done: the next zone's unlock quest (it needs the story finished)
    // or the zone's "Making the Most of ..." achievement. Hydatos has no later quest, so only its achievements.
    private static readonly (FieldZone Zone, uint QuestId, int AchievementId, int LastStepId)[] ZoneDone =
    {
        (FieldZone.Anemos, 68478, 2045, 5),
        (FieldZone.Pagos, 68148, 2097, 11),
        (FieldZone.Pyros, 68149, 2159, 16),
        (FieldZone.Hydatos, 0, 2231, 21),
        (FieldZone.Hydatos, 0, 2242, 22),
    };

    /// <summary>Krile's spot at a zone's base camp.</summary>
    public static StoryMark Krile(FieldZone zone) => zone switch
    {
        FieldZone.Pagos => KrilePagos,
        FieldZone.Pyros => KrilePyros,
        FieldZone.Hydatos => KrileHydatos,
        _ => KrileAnemos,
    };

    /// <summary>True once the game's achievements have loaded (after the Achievements window is opened once).</summary>
    public static unsafe bool AchievementsLoaded()
    {
        var a = Achievement.Instance();
        return a != null && a->IsLoaded();
    }

    /// <summary>The last step id known to be done: the higher of what the player ticked and what the game proves.</summary>
    public static int DoneThrough(CharacterProgress p) => System.Math.Max(p.EurekaStoryDone, ProvenThrough());

    /// <summary>The last step id the game proves done, from zone unlock quests and achievements (0 = none).</summary>
    public static unsafe int ProvenThrough()
    {
        var done = 0;
        var ach = Achievement.Instance();
        var achLoaded = ach != null && ach->IsLoaded();
        foreach (var z in ZoneDone)
        {
            var proven = (z.QuestId != 0 && QuestManager.IsQuestComplete(z.QuestId)) ||
                         (achLoaded && ach->IsComplete(z.AchievementId));
            if (proven && z.LastStepId > done)
                done = z.LastStepId;
        }

        return done;
    }

    /// <summary>The first step not done yet, or null when the whole story is finished.</summary>
    public static EurekaStoryStep? Next(CharacterProgress p)
    {
        var done = DoneThrough(p);
        foreach (var s in Steps)
            if (s.Id > done)
                return s;
        return null;
    }

    // The story objects (EObj ids, each with its own CtsErkEureka* event) and the step that counts as done once
    // you've used one. An object partway through a step marks the step before it done.
    private static readonly Dictionary<uint, int> StepObjects = new()
    {
        [2009416] = 2, [2009417] = 3, [2009418] = 4, [2009419] = 4, [2009415] = 5,  // Anemos confluences, Aethernet relay
        [2009550] = 7, [2009551] = 8, [2009552] = 8, [2009566] = 9, [2009553] = 11, // Pagos aetherial streams, Fumarole
        [2009619] = 12, [2009624] = 12, [2009620] = 13, [2009621] = 14,             // Pyros confluences, promising scrap
        [2009626] = 15, [2009627] = 16,                                             // East Flamerock, Cavern of the Second Cant
        [2009686] = 18, [2009687] = 19, [2009688] = 20, [2009689] = 21,             // Hydatos pooled aether, scattered tomes
    };

    /// <summary>
    /// The step done by the story object you're using right now (targeted, or within a few yalms, while an event
    /// is running), or 0. Ignores objects for steps above your Elemental Level, since the game won't advance those.
    /// </summary>
    public static int StepFromEvent(CharacterProgress p)
    {
        if (!Service.Condition[ConditionFlag.OccupiedInEvent] && !Service.Condition[ConditionFlag.OccupiedInCutSceneEvent])
            return 0;

        var obj = Service.TargetManager.Target;
        if (obj is null || !StepObjects.ContainsKey(obj.BaseId))
        {
            obj = null;
            var me = Service.ObjectTable.LocalPlayer;
            if (me is null)
                return 0;
            foreach (var o in Service.ObjectTable)
                if (o.ObjectKind == ObjectKind.EventObj && StepObjects.ContainsKey(o.BaseId) &&
                    Vector3.Distance(o.Position, me.Position) < 5f)
                    obj = o;
        }

        if (obj is null)
            return 0;
        var step = StepObjects[obj.BaseId];
        return p.ElementalLevel >= Steps[step - 1].Level ? step : 0;
    }

    // The step already flagged in this visit, so the flag and chat line happen once, not every second.
    private static int flaggedStep;

    /// <summary>
    /// Flags the next story step once you're in its zone and at its level. Called about once a second
    /// from <see cref="FieldProgress.Update"/>; does nothing if that step was already flagged this visit.
    /// </summary>
    public static void AutoFlag(FieldZone zone, CharacterProgress p)
    {
        var next = Next(p);
        if (next is null || next.Zone != zone || p.ElementalLevel < next.Level || next.Id == flaggedStep)
            return;

        flaggedStep = next.Id;
        Flag(next, openMap: false, announce: true);
    }

    /// <summary>Leaving Eureka resets the auto-flag, so the next visit flags the step again.</summary>
    public static void ForgetFlag() => flaggedStep = 0;

    /// <summary>Puts the game's map flag on the step's spot, optionally opening the map and posting a chat link.</summary>
    public static unsafe void Flag(EurekaStoryStep step, bool openMap, bool announce)
    {
        var mark = step.Flag ?? Krile(step.Zone);
        var territory = TerritoryId(step.Zone);
        var map = MapId(step.Zone);

        var agent = AgentMap.Instance();
        if (agent != null)
        {
            agent->SetFlagMapMarker(territory, map, ToWorld(step.Zone, mark));
            if (openMap)
                agent->OpenMapByMapId(map, territory);
        }

        if (!announce)
            return;

        var msg = new SeStringBuilder()
            .AddText($"[Mercury] Eureka story (Elemental Lv {step.Level}): {step.Todo} Flagged {mark.Name}: ")
            .Append(SeString.CreateMapLink(territory, map, mark.X, mark.Y))
            .Build();
        Service.ChatGui.Print(msg);
    }

    /// <summary>World position for a map coordinate, using the zone map's scale and offset.</summary>
    public static Vector3 ToWorld(FieldZone zone, StoryMark mark)
    {
        var map = Service.DataManager.GetExcelSheet<MapSheet>().GetRow(MapId(zone));
        var c = map.SizeFactor / 100f;
        float World(float coord, short offset) => ((((coord - 1f) * c / 41f * 2048f) - 1024f) / c) - offset;
        return new Vector3(World(mark.X, map.OffsetX), 0f, World(mark.Y, map.OffsetY));
    }

    public static uint TerritoryId(FieldZone zone) => zone switch
    {
        FieldZone.Pagos => 763,
        FieldZone.Pyros => 795,
        FieldZone.Hydatos => 827,
        _ => 732,
    };

    public static uint MapId(FieldZone zone) => zone switch
    {
        FieldZone.Pagos => 467,
        FieldZone.Pyros => 484,
        FieldZone.Hydatos => 515,
        _ => 414,
    };
}
