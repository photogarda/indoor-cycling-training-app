using Trainer.Core.Models;

namespace Trainer.Core.Planning;

/// <summary>Everything the plan engine needs. The engine is pure: same input, same plan.</summary>
public class PlanInput
{
    public required Athlete Athlete { get; init; }

    /// <summary>First day the engine may plan. Anything earlier is frozen.</summary>
    public required DateOnly Start { get; init; }

    public IReadOnlyList<Race> Races { get; init; } = [];
    public IReadOnlyList<BlockedDay> BlockedDays { get; init; } = [];
    public IReadOnlyList<FtpEntry> FtpHistory { get; init; } = [];
    public required IReadOnlyList<WorkoutTemplate> Templates { get; init; }

    /// <summary>
    /// Current (non-superseded) planned workouts. Those before <see cref="Start"/> and locked ones are frozen
    /// and planned around; the rest are replaced.
    /// </summary>
    public IReadOnlyList<PlannedWorkout> Existing { get; init; } = [];

    /// <summary>Daily load history up to the day before <see cref="Start"/>, for CTL and the fatigue guard.</summary>
    public IReadOnlyList<DailyLoad> Loads { get; init; } = [];

    /// <summary>Week starts (Mondays) of completed weeks judged bad: 3+ missed or under 60 % of planned TSS.</summary>
    public IReadOnlySet<DateOnly> BadWeeks { get; init; } = new HashSet<DateOnly>();

    /// <summary>
    /// When training towards the current target A race began (the first plan made for it). Keeps phase
    /// boundaries stable as the plan is regenerated week after week.
    /// </summary>
    public DateOnly? SeasonStart { get; init; }

    public int MinWeeksAhead { get; init; } = 8;

    /// <summary>Rider-chosen plan period. No workouts before <see cref="PlanFrom"/> or after <see cref="PlanTo"/>.</summary>
    public DateOnly? PlanFrom { get; init; }
    public DateOnly? PlanTo { get; init; }
}

public class PlanResult
{
    public DateOnly Start { get; init; }
    public DateOnly End { get; init; }
    public int? TargetRaceId { get; init; }
    public List<PlanWeek> Weeks { get; } = [];
    /// <summary>New workouts from <see cref="PlanInput.Start"/> on. Frozen workouts are not included.</summary>
    public List<PlannedWorkout> Workouts { get; } = [];
    public List<string> Notes { get; } = [];
}
