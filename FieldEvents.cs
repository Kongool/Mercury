using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;

namespace Mercury;

/// <summary>Finds the Critical Encounter / Critical Engagement container for whichever field op you are in.</summary>
public static class FieldEvents
{
    public static unsafe DynamicEventContainer* Container()
    {
        var c = DynamicEventContainer.GetInstance();
        if (c != null)
            return c;

        // Bozja keeps its container inside its own director.
        var bozja = PublicContentBozja.GetInstance();
        return bozja != null ? &bozja->DynamicEventContainer : null;
    }
}
