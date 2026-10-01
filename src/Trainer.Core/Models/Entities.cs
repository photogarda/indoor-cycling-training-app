namespace Trainer.Core.Models;

// Plain classes shared by the engine and the database. Trainer.Data maps them with fluent configuration,
// so nothing here depends on EF Core.

public class Athlete
{
    public int Id { get; set; }
    public string Name { get; set; } = "Rider";
    public AthleteLevel Level { get; set; } = AthleteLevel.Amateur;
    public double WeeklyHours { get; set; } = 8;
    public List<DayOfWeek> TrainingDays { get; set; } =
        [DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Saturday, DayOfWeek.Sunday];
    public DayOfWeek LongRideDay { get; set; } = DayOfWeek.Saturday;
    public NoRaceGoal NoRaceGoal { get; set; } = NoRaceGoal.Ftp;
    public bool DefaultIndoor { get; set; } = true;
    public int? ThresholdHr { get; set; }
    /// <summary>Fixed date that anchors rolling no-race 3+1 cycles so regenerating doesn't shift recovery weeks.</summary>
    public DateOnly PlanAnchor { get; set; } = new(2026, 1, 5);

    // Integrations
    public string? EdgePath { get; set; }
    public string? WatchFolder { get; set; }
    public string? StravaClientId { get; set; }
    public string? StravaClientSecret { get; set; }
    public string? StravaRefreshToken { get; set; }
    public string? StravaAccessToken { get; set; }
    public DateTime? StravaTokenExpiresUtc { get; set; }
    public DateTime? StravaLastSyncUtc { get; set; }
}

public class FtpEntry
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }
    public int Watts { get; set; }
    public FtpMethod Method { get; set; }
}

public class Race
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }
    public string Name { get; set; } = "";
    public EventType Type { get; set; }
    public double DurationHours { get; set; } = 2;
    /// <summary>1–10. Scales how much hard work the plan prescribes.</summary>
    public int Intensity { get; set; } = 6;
    public RacePriority Priority { get; set; } = RacePriority.A;
}

public class BlockedDay
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }
    public string Reason { get; set; } = "";
}

public class Plan
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public int? TargetRaceId { get; set; }
    public bool IsActive { get; set; }
    public List<PlanWeek> Weeks { get; set; } = [];
}

public class PlanWeek
{
    public int Id { get; set; }
    public int PlanId { get; set; }
    public DateOnly WeekStart { get; set; }
    public Phase Phase { get; set; }
    public WeekType WeekType { get; set; }
    public double TargetHours { get; set; }
    public double TargetTss { get; set; }
    /// <summary>Projected CTL at the end of the week if the plan is followed.</summary>
    public double PlannedCtl { get; set; }
    public int? RaceId { get; set; }
}

public class PlannedWorkout
{
    public int Id { get; set; }
    public int? PlanId { get; set; }
    public DateOnly Date { get; set; }
    public int? TemplateId { get; set; }
    public string Name { get; set; } = "";
    public WorkoutKind Kind { get; set; }
    public bool IsKey { get; set; }
    /// <summary>Steps already sized to the planned duration, in % FTP.</summary>
    public List<WorkoutStep> Steps { get; set; } = [];
    public int DurationSec { get; set; }
    public double Tss { get; set; }
    public double IntensityFactor { get; set; }
    public bool Indoor { get; set; } = true;
    public WorkoutStatus Status { get; set; } = WorkoutStatus.Planned;
    /// <summary>Edited by hand: the engine keeps it as is.</summary>
    public bool Locked { get; set; }
    /// <summary>Replaced by a newer plan version. Kept only for history.</summary>
    public bool Superseded { get; set; }
    public string? Notes { get; set; }

    public TimeSpan Duration => TimeSpan.FromSeconds(DurationSec);
}

public class WorkoutTemplate
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public WorkoutKind Kind { get; set; }
    public string Description { get; set; } = "";
    public List<WorkoutStep> Steps { get; set; } = [];
    public AthleteLevel MinLevel { get; set; } = AthleteLevel.Basic;
    public AthleteLevel MaxLevel { get; set; } = AthleteLevel.Pro;
    public bool BuiltIn { get; set; }
}

public class Activity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public DateTime StartTime { get; set; }
    public ActivitySource Source { get; set; }
    public int DurationSec { get; set; }
    public double? DistanceKm { get; set; }
    public double? AvgPower { get; set; }
    public double? NormalizedPower { get; set; }
    public double? AvgHr { get; set; }
    public double? AvgCadence { get; set; }
    public double Tss { get; set; }
    public double? IntensityFactor { get; set; }
    /// <summary>True when TSS came from heart rate because the ride had no power.</summary>
    public bool HrBasedTss { get; set; }
    public string? FilePath { get; set; }
    public long? StravaId { get; set; }
    public int? PlannedWorkoutId { get; set; }
    /// <summary>Mean-maximal power by duration in seconds (5 s … 60 min).</summary>
    public Dictionary<int, double> PowerCurve { get; set; } = [];

    public DateOnly Date => DateOnly.FromDateTime(StartTime);
}

public class DailyLoad
{
    public DateOnly Date { get; set; }
    public double Tss { get; set; }
    public double Ctl { get; set; }
    public double Atl { get; set; }
    public double Tsb { get; set; }
}
