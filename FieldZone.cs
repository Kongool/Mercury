namespace Mercury;

/// <summary>A field-operation zone Mercury knows about.</summary>
public enum FieldZone
{
    None,
    SouthHorn,
    NorthHorn,
    Anemos,
    Pagos,
    Pyros,
    Hydatos,
    SouthernFront,
    Zadnor,
}

/// <summary>The field operation a zone belongs to.</summary>
public enum FieldOp
{
    None,
    Occult,
    Eureka,
    Bozja,
}

/// <summary>Territory ids, names, level ranges and weather tables for every supported zone.</summary>
public static class FieldZones
{
    public static FieldZone Current => FromTerritory(Service.ClientState.TerritoryType);

    public static FieldZone FromTerritory(uint territoryType) => territoryType switch
    {
        MapCatalog.SouthHornTerritory => FieldZone.SouthHorn,
        MapCatalog.NorthHornTerritory => FieldZone.NorthHorn,
        732 => FieldZone.Anemos,
        763 => FieldZone.Pagos,
        795 => FieldZone.Pyros,
        827 => FieldZone.Hydatos,
        920 => FieldZone.SouthernFront,
        975 => FieldZone.Zadnor,
        _ => FieldZone.None,
    };

    public static FieldOp OpOf(FieldZone zone) => zone switch
    {
        FieldZone.SouthHorn or FieldZone.NorthHorn => FieldOp.Occult,
        FieldZone.Anemos or FieldZone.Pagos or FieldZone.Pyros or FieldZone.Hydatos => FieldOp.Eureka,
        FieldZone.SouthernFront or FieldZone.Zadnor => FieldOp.Bozja,
        _ => FieldOp.None,
    };

    public static string DisplayName(FieldZone zone) => zone switch
    {
        FieldZone.SouthHorn => "South Horn",
        FieldZone.NorthHorn => "North Horn",
        FieldZone.Anemos => "Eureka Anemos",
        FieldZone.Pagos => "Eureka Pagos",
        FieldZone.Pyros => "Eureka Pyros",
        FieldZone.Hydatos => "Eureka Hydatos",
        FieldZone.SouthernFront => "Bozjan Southern Front",
        FieldZone.Zadnor => "Zadnor",
        _ => "Not in a field op",
    };

    /// <summary>Elemental Level range each Eureka zone syncs to (min, cap).</summary>
    public static (int Min, int Cap) EurekaRange(FieldZone zone) => zone switch
    {
        FieldZone.Anemos => (1, 20),
        FieldZone.Pagos => (20, 35),
        FieldZone.Pyros => (35, 50),
        FieldZone.Hydatos => (50, 60),
        _ => (0, 0),
    };

    public static readonly FieldZone[] EurekaZones = { FieldZone.Anemos, FieldZone.Pagos, FieldZone.Pyros, FieldZone.Hydatos };

    /// <summary>WeatherRate sheet row for the zone, or 0 when Mercury has no forecast for it.</summary>
    public static uint WeatherRate(FieldZone zone) => zone switch
    {
        FieldZone.Anemos => 91,
        FieldZone.Pagos => 94,
        FieldZone.Pyros => 96,
        FieldZone.Hydatos => 100,
        FieldZone.SouthernFront => 124,
        FieldZone.Zadnor => 130,
        _ => 0,
    };
}
