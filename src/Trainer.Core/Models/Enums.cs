namespace Trainer.Core.Models;

public enum AthleteLevel { Basic, Amateur, Advanced, Pro }

/// <summary>What the plan works on when no race is on the calendar.</summary>
public enum NoRaceGoal { Fitness, Ftp }

public enum FtpMethod { Manual, Ramp, Estimate }

public enum EventType
{
    RoadRace,
    Crit,
    TimeTrial,
    GranFondo,
    Gravel,
    Cyclocross,
    StageRace,
    MtbXc,
    MtbMarathon,
    IndoorEvent,
}

public enum RacePriority { A, B, C }

public enum Phase
{
    /// <summary>Rolling no-race blocks (and any weeks earlier than the base cap allows).</summary>
    General,
    Base,
    Build,
    Specialty,
    Taper,
    /// <summary>The week after an A race.</summary>
    Recovery,
}

public enum WeekType { Load, Recovery }

public enum WorkoutStatus { Planned, Done, Partial, Missed, Moved }

/// <summary>Training purpose of a workout. Drives selection in the plan engine and colours in the UI.</summary>
public enum WorkoutKind
{
    Recovery,
    Endurance,
    EnduranceSurges,
    LongRide,
    Tempo,
    LongTempo,
    SweetSpot,
    Threshold,
    ThresholdClimbs,
    OverUnder,
    Vo2Max,
    Vo2Short,
    ThirtyThirty,
    Anaerobic,
    AnaerobicStarts,
    Sprint,
    RaceSim,
    Opener,
    RampTest,
}

public enum StepKind
{
    Warmup,
    Work,
    Recovery,
    Cooldown,
    /// <summary>Endurance filler that stretches or shrinks to fit a target duration.</summary>
    Filler,
    /// <summary>ERG off: no power target (sprints, race simulation).</summary>
    Free,
    Repeat,
}

public enum ActivitySource { Fit, Strava }

public static class EnumText
{
    public static string Display(this EventType t) => t switch
    {
        EventType.RoadRace => "Road race",
        EventType.Crit => "Crit",
        EventType.TimeTrial => "Time trial",
        EventType.GranFondo => "Gran fondo",
        EventType.Gravel => "Gravel",
        EventType.Cyclocross => "Cyclocross",
        EventType.StageRace => "Stage race",
        EventType.MtbXc => "MTB XC",
        EventType.MtbMarathon => "MTB marathon",
        EventType.IndoorEvent => "Indoor event",
        _ => t.ToString(),
    };

    public static string Display(this WorkoutKind k) => k switch
    {
        WorkoutKind.EnduranceSurges => "Endurance + surges",
        WorkoutKind.LongRide => "Long ride",
        WorkoutKind.LongTempo => "Long tempo",
        WorkoutKind.SweetSpot => "Sweet spot",
        WorkoutKind.ThresholdClimbs => "Threshold climbs",
        WorkoutKind.OverUnder => "Over-unders",
        WorkoutKind.Vo2Max => "VO2 max",
        WorkoutKind.Vo2Short => "VO2 short (30/15)",
        WorkoutKind.ThirtyThirty => "30/30s",
        WorkoutKind.AnaerobicStarts => "Anaerobic starts",
        WorkoutKind.RaceSim => "Race simulation",
        WorkoutKind.RampTest => "Ramp test",
        _ => k.ToString(),
    };

    /// <summary>Key (hard) sessions count against the weekly key budget and are never placed on consecutive days.</summary>
    public static bool IsKey(this WorkoutKind k) => k is not (WorkoutKind.Recovery or WorkoutKind.Endurance
        or WorkoutKind.EnduranceSurges or WorkoutKind.LongRide or WorkoutKind.Opener);
}
