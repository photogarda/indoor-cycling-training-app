using Trainer.Core.Models;

namespace Trainer.Core.Planning;

public static class RaceRules
{
    /// <summary>Returns an error message, or null when the race is valid against the others.</summary>
    public static string? Validate(Race race, IEnumerable<Race> others)
    {
        if (string.IsNullOrWhiteSpace(race.Name)) return "Give the race a name.";
        if (race.DurationHours is <= 0 or > 24) return "Duration must be between 0 and 24 hours.";
        if (race.Intensity is < 1 or > 10) return "Intensity must be between 1 and 10.";
        if (race.Priority != RacePriority.A) return null;
        foreach (var o in others)
        {
            if (o.Id == race.Id && race.Id != 0) continue;
            if (o.Priority != RacePriority.A) continue;
            if (race.Date < o.Date.AddMonths(3) && o.Date < race.Date.AddMonths(3))
                return $"A races must be at least 3 months apart: \"{o.Name}\" is on {o.Date:d MMM yyyy}. " +
                       "Make one of them a B race or move it.";
        }
        return null;
    }

    /// <summary>Rough race TSS for load projections, from duration and the 1–10 intensity.</summary>
    public static double EstimatedTss(Race race)
    {
        var intensity = Math.Clamp(0.65 + 0.035 * race.Intensity, 0.7, 1.05);
        return Math.Round(race.DurationHours * intensity * intensity * 100);
    }

    public static (double Hours, string? LongRideHint) SpecialtyLongRide(Race race, double defaultHours) => race.Type switch
    {
        EventType.Crit => (2, null),
        EventType.RoadRace => (Math.Min(race.DurationHours, 5), "race-pace"),
        EventType.TimeTrial => (2.25, null),
        EventType.GranFondo => (Math.Min(race.DurationHours, 5), "tempo"),
        EventType.Gravel => (Math.Min(race.DurationHours, 5), "Fueling"),
        EventType.MtbMarathon => (Math.Min(race.DurationHours, 5), "Fueling"),
        EventType.MtbXc => (2.5, null),
        EventType.Cyclocross => (1.75, null),
        EventType.StageRace => (defaultHours, "tempo"),
        EventType.IndoorEvent => (2, null),
        _ => (defaultHours, null),
    };

    /// <summary>Specialty key sessions by event type, in priority order.</summary>
    public static IReadOnlyList<KeySlot> SpecialtyKeys(EventType type) => type switch
    {
        EventType.Crit => [new(WorkoutKind.ThirtyThirty), new(WorkoutKind.Anaerobic, "1-min"), new(WorkoutKind.Sprint)],
        EventType.RoadRace => [new(WorkoutKind.Vo2Max, "×4"), new(WorkoutKind.OverUnder), new(WorkoutKind.RaceSim, "Road")],
        EventType.TimeTrial => [new(WorkoutKind.Threshold), new(WorkoutKind.SweetSpot), new(WorkoutKind.Threshold)],
        EventType.GranFondo => [new(WorkoutKind.SweetSpot), new(WorkoutKind.Tempo), new(WorkoutKind.ThresholdClimbs)],
        EventType.Gravel => [new(WorkoutKind.OverUnder), new(WorkoutKind.LongTempo), new(WorkoutKind.Threshold)],
        EventType.MtbMarathon => [new(WorkoutKind.ThresholdClimbs), new(WorkoutKind.OverUnder), new(WorkoutKind.Vo2Max)],
        EventType.MtbXc => [new(WorkoutKind.Vo2Max, "3-min"), new(WorkoutKind.Vo2Short), new(WorkoutKind.AnaerobicStarts)],
        EventType.Cyclocross => [new(WorkoutKind.ThirtyThirty), new(WorkoutKind.Anaerobic, "1-min"), new(WorkoutKind.AnaerobicStarts)],
        EventType.StageRace => [new(WorkoutKind.SweetSpot), new(WorkoutKind.Threshold), new(WorkoutKind.OverUnder)],
        EventType.IndoorEvent => [new(WorkoutKind.RaceSim, "Indoor"), new(WorkoutKind.Vo2Max), new(WorkoutKind.Vo2Short)],
        _ => [new(WorkoutKind.Threshold), new(WorkoutKind.Vo2Max), new(WorkoutKind.OverUnder)],
    };
}

/// <summary>A key-session slot: the workout kind and an optional name hint to prefer.</summary>
public readonly record struct KeySlot(WorkoutKind Kind, string? Hint = null);
