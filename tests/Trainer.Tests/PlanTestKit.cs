using System.Text;
using Trainer.Core.Models;
using Trainer.Core.Planning;
using Trainer.Core.Workouts;

namespace Trainer.Tests;

/// <summary>Fixed calendars for engine tests. Today is Thursday 1 October 2026.</summary>
public static class PlanTestKit
{
    public static readonly DateOnly Today = new(2026, 10, 1);
    public static readonly List<WorkoutTemplate> Library = WorkoutLibrary.BuiltIn();

    public static Athlete Athlete(AthleteLevel level = AthleteLevel.Amateur, double hours = 10, params DayOfWeek[] days) => new()
    {
        Name = "Test",
        Level = level,
        WeeklyHours = hours,
        TrainingDays = days.Length > 0 ? [.. days] : [DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Saturday, DayOfWeek.Sunday],
        LongRideDay = DayOfWeek.Saturday,
        NoRaceGoal = NoRaceGoal.Ftp,
    };

    public static List<FtpEntry> Ftp => [new() { Date = new DateOnly(2026, 9, 1), Watts = 250, Method = FtpMethod.Ramp }];

    public static Race Race(int id, DateOnly date, RacePriority p, EventType type = EventType.RoadRace, double hours = 3, int intensity = 7) =>
        new() { Id = id, Date = date, Name = $"Race {id}", Priority = p, Type = type, DurationHours = hours, Intensity = intensity };

    public static PlanResult Generate(Athlete athlete, IReadOnlyList<Race>? races = null, IReadOnlyList<BlockedDay>? blocked = null,
        IReadOnlyList<PlannedWorkout>? existing = null, DateOnly? start = null, IReadOnlyList<DailyLoad>? loads = null,
        IReadOnlySet<DateOnly>? badWeeks = null, DateOnly? seasonStart = null, IReadOnlyList<FtpEntry>? ftp = null) =>
        PlanEngine.Generate(new PlanInput
        {
            Athlete = athlete,
            Start = start ?? Today,
            Races = races ?? [],
            BlockedDays = blocked ?? [],
            FtpHistory = ftp ?? Ftp,
            Templates = Library,
            Existing = existing ?? [],
            Loads = loads ?? [],
            BadWeeks = badWeeks ?? new HashSet<DateOnly>(),
            SeasonStart = seasonStart,
        });

    public static string Describe(PlanResult plan)
    {
        var sb = new StringBuilder();
        foreach (var w in plan.Weeks)
        {
            sb.AppendLine($"{w.WeekStart:yyyy-MM-dd} {w.Phase,-9} {w.WeekType,-8} {w.TargetHours,5:0.0} h {w.TargetTss,5:0} TSS CTL {w.PlannedCtl,5:0.0}");
            foreach (var x in plan.Workouts.Where(x => x.Date >= w.WeekStart && x.Date < w.WeekStart.AddDays(7)))
                sb.AppendLine($"    {x.Date:ddd dd} {(x.IsKey ? "*" : " ")} {x.Name,-28} {x.Duration:h\\:mm} {x.Tss,5:0} {x.Notes}");
        }
        foreach (var n in plan.Notes) sb.AppendLine("NOTE " + n);
        return sb.ToString();
    }
}
