using System;
using System.Collections.Generic;
using Lumina.Excel.Sheets;

namespace Mercury;

/// <summary>A real-time interval during which one weather is active in a zone.</summary>
public readonly record struct ZoneWeatherWindow(string Name, int Chance, DateTimeOffset Start, DateTimeOffset End);

/// <summary>
/// Deterministic weather forecast for any zone, read from the game's WeatherRate sheet. Uses the same
/// roll as <see cref="OccultWeatherForecast"/>; the North Horn keeps its own table for its weather mobs.
/// </summary>
public static class ZoneWeather
{
    private static readonly Dictionary<uint, List<(string Name, int Chance)>> Cache = new();

    public static IReadOnlyList<(string Name, int Chance)> Patterns(uint rateId)
    {
        if (Cache.TryGetValue(rateId, out var cached))
            return cached;

        var list = new List<(string, int)>();
        var row = Service.DataManager.GetExcelSheet<WeatherRate>().GetRowOrDefault(rateId);
        if (row is { } r)
        {
            for (var i = 0; i < r.Weather.Count && i < r.Rate.Count; i++)
            {
                var chance = r.Rate[i];
                if (chance == 0 || !r.Weather[i].IsValid)
                    continue;
                list.Add((r.Weather[i].Value.Name.ExtractText(), chance));
            }
        }

        Cache[rateId] = list;
        return list;
    }

    /// <summary>Consecutive forecast periods for the zone, starting with the current one.</summary>
    public static IReadOnlyList<ZoneWeatherWindow> Upcoming(uint rateId, DateTimeOffset from, int count)
    {
        var result = new List<ZoneWeatherWindow>(count);
        var patterns = Patterns(rateId);
        if (patterns.Count == 0)
            return result;

        var unix = from.ToUnixTimeSeconds();
        var start = unix - (unix % OccultWeatherForecast.PeriodSeconds);
        for (var i = 0; i < count; i++)
        {
            var s = start + (i * OccultWeatherForecast.PeriodSeconds);
            var target = OccultWeatherForecast.CalculateForecastTarget(s);
            var (name, chance) = Pick(patterns, target);
            result.Add(new ZoneWeatherWindow(
                name,
                chance,
                DateTimeOffset.FromUnixTimeSeconds(s),
                DateTimeOffset.FromUnixTimeSeconds(s + OccultWeatherForecast.PeriodSeconds)));
        }

        return result;
    }

    private static (string Name, int Chance) Pick(IReadOnlyList<(string Name, int Chance)> patterns, int target)
    {
        var threshold = 0;
        foreach (var p in patterns)
        {
            threshold += p.Chance;
            if (target < threshold)
                return p;
        }

        return patterns[^1];
    }
}
