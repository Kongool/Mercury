using System.Collections.Generic;

namespace Mercury;

/// <summary>
/// A Phantom Blue Mage spell and how it's learned. Unlike other phantom jobs, Phantom Blue
/// Mage learns most of its actions from specific North Horn enemies (chance on their death),
/// so this carries the teaching enemy and, for overworld mobs, its map coordinate.
/// </summary>
public sealed record BlueSpell(
    string Name,
    int Level,
    string LearnFrom,
    string? Enemy = null,
    float MapX = 0f,
    float MapY = 0f);

/// <summary>
/// The Phantom Blue Mage spell list with learn sources, from the game's MKDSupportJob
/// (job 21) and the community wiki. All learn locations are in The North Horn.
/// </summary>
public static class PhantomBlueMage
{
    public static readonly IReadOnlyList<BlueSpell> Spells = new[]
    {
        new BlueSpell("Occult Aero", 1, "Unlocked by default"),
        new BlueSpell("Occult Missile", 1, "Pallmagia - \"Appalling Behavior\" CE", "Pallmagia"),
        new BlueSpell("Occult Aqua Breath", 1, "Crescent Stoneshell - North Horn (31, 8)", "Crescent Stoneshell", 31f, 8f),
        new BlueSpell("Occult Aero II", 2, "Crescent Anila - North Horn (16, 37); replaces Aero", "Crescent Anila", 16f, 37f),
        new BlueSpell("Occult Mighty Guard", 2, "Crescent Bibliotaph - North Horn (38, 31)", "Crescent Bibliotaph", 38f, 31f),
        new BlueSpell("Occult Aero III", 3, "Alabaster Blade - \"Quarried Away\" CE; needs Aero II", "Alabaster Blade"),
        new BlueSpell("Occult White Wind", 3, "Crescent Flame - North Horn (5, 36)", "Crescent Flame", 5f, 36f),
    };
}
