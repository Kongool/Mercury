using System;
using System.Collections.Generic;

namespace Mercury;

/// <summary>A North Horn weather result and the mob that is present during it.</summary>
public sealed record OccultWeatherPattern(string Name, int Chance, string? Mob);

/// <summary>A real-time interval during which one North Horn weather result is active.</summary>
public readonly record struct OccultWeatherWindow(
    OccultWeatherPattern Pattern,
    DateTimeOffset Start,
    DateTimeOffset End);

/// <summary>
/// Deterministic Occult Crescent weather forecast. FFXIV changes weather every eight
/// Eorzean hours (1,400 real-world seconds) and derives the result from the Unix timestamp.
/// </summary>
public static class OccultWeatherForecast
{
    public const long PeriodSeconds = 1_400;

    // North Horn WeatherRate row 168. Keep these in game-data order; Chance is a weight,
    // not a cumulative threshold.
    public static readonly IReadOnlyList<OccultWeatherPattern> Patterns = new[]
    {
        new OccultWeatherPattern("Clear Skies", 10, "Crescent Bomb"),
        new OccultWeatherPattern("Fair Skies", 45, null),
        new OccultWeatherPattern("Clouds", 15, "Crescent Dhruva"),
        new OccultWeatherPattern("Rain", 10, "Crescent Mousse"),
        new OccultWeatherPattern("Atmospheric Phantasms", 15, "Crescent Mimic"),
        new OccultWeatherPattern("Illusory Disturbances", 5, "Crescent Fool"),
    };

    /// <summary>Returns the weather period containing <paramref name="when"/>.</summary>
    public static OccultWeatherWindow At(DateTimeOffset when)
    {
        var startUnix = PeriodStart(when.ToUnixTimeSeconds());
        return WindowAt(startUnix);
    }

    /// <summary>Returns consecutive forecast periods, starting with the current period.</summary>
    public static IReadOnlyList<OccultWeatherWindow> Upcoming(DateTimeOffset from, int count)
    {
        var result = new List<OccultWeatherWindow>(Math.Max(0, count));
        var startUnix = PeriodStart(from.ToUnixTimeSeconds());

        for (var i = 0; i < count; i++)
            result.Add(WindowAt(startUnix + (i * PeriodSeconds)));

        return result;
    }

    /// <summary>
    /// Finds upcoming windows for one weather and joins adjacent identical periods into a
    /// single continuous window. The active window is returned first when it matches.
    /// </summary>
    public static IReadOnlyList<OccultWeatherWindow> NextWindows(
        DateTimeOffset from,
        string weatherName,
        int count)
    {
        var result = new List<OccultWeatherWindow>(Math.Max(0, count));
        var startUnix = PeriodStart(from.ToUnixTimeSeconds());

        // 4,096 periods covers more than 66 real-world days and keeps malformed calls bounded.
        for (var i = 0; i < 4_096 && result.Count < count; i++)
        {
            var window = WindowAt(startUnix + (i * PeriodSeconds));
            if (!window.Pattern.Name.Equals(weatherName, StringComparison.Ordinal))
                continue;

            if (result.Count > 0 && result[^1].End == window.Start)
            {
                var previous = result[^1];
                result[^1] = previous with { End = window.End };
            }
            else
            {
                result.Add(window);
            }
        }

        return result;
    }

    /// <summary>FFXIV's standard weather roll for a Unix timestamp, from 0 through 99.</summary>
    public static int CalculateForecastTarget(long unixSeconds)
    {
        var bell = unixSeconds / 175;
        var increment = (uint)((bell + 8 - (bell % 8)) % 24);
        var totalDays = unchecked((uint)(unixSeconds / 4_200));
        var calculationBase = unchecked((totalDays * 100u) + increment);
        var step1 = unchecked((calculationBase << 11) ^ calculationBase);
        var step2 = unchecked((step1 >> 8) ^ step1);
        return (int)(step2 % 100u);
    }

    private static OccultWeatherWindow WindowAt(long startUnix)
    {
        var pattern = PatternForTarget(CalculateForecastTarget(startUnix));
        return new OccultWeatherWindow(
            pattern,
            DateTimeOffset.FromUnixTimeSeconds(startUnix),
            DateTimeOffset.FromUnixTimeSeconds(startUnix + PeriodSeconds));
    }

    private static OccultWeatherPattern PatternForTarget(int target)
    {
        var threshold = 0;
        foreach (var pattern in Patterns)
        {
            threshold += pattern.Chance;
            if (target < threshold)
                return pattern;
        }

        // The configured weights total 100, so this is only reachable if the table is edited
        // incorrectly. A stable fallback is preferable to crashing the UI draw loop.
        return Patterns[^1];
    }

    private static long PeriodStart(long unixSeconds)
        => unixSeconds - (unixSeconds % PeriodSeconds);
}
