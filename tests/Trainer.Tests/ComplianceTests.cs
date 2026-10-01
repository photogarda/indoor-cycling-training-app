using Trainer.Core.Adaptation;
using Trainer.Core.Models;

namespace Trainer.Tests;

public class ComplianceTests
{
    private static readonly DateOnly Today = new(2026, 10, 1);

    [Theory]
    [InlineData(100, 85, WorkoutStatus.Done)]
    [InlineData(100, 80, WorkoutStatus.Done)]
    [InlineData(100, 65, WorkoutStatus.Partial)]
    [InlineData(100, 49, WorkoutStatus.Missed)]
    public void Status_from_actual_vs_planned_tss(double planned, double actual, WorkoutStatus expected) =>
        Assert.Equal(expected, Compliance.Evaluate(planned, actual, Today.AddDays(-1), Today));

    [Fact]
    public void Nothing_by_midnight_is_missed_but_today_is_still_planned()
    {
        Assert.Equal(WorkoutStatus.Missed, Compliance.Evaluate(80, null, Today.AddDays(-1), Today));
        Assert.Equal(WorkoutStatus.Planned, Compliance.Evaluate(80, null, Today, Today));
    }

    [Fact]
    public void Matches_same_day_first_then_one_day_either_side()
    {
        List<PlannedWorkout> plan =
        [
            new() { Id = 1, Date = new(2026, 9, 22), DurationSec = 5400, IsKey = true },
            new() { Id = 2, Date = new(2026, 9, 24), DurationSec = 5400, IsKey = true },
            new() { Id = 3, Date = new(2026, 9, 26), DurationSec = 10800 },
        ];
        List<Activity> rides =
        [
            new() { Id = 10, StartTime = new DateTime(2026, 9, 22, 18, 0, 0), DurationSec = 5300 },
            new() { Id = 11, StartTime = new DateTime(2026, 9, 25, 18, 0, 0), DurationSec = 5300 }, // a day late
            new() { Id = 12, StartTime = new DateTime(2026, 9, 26, 9, 0, 0), DurationSec = 10000 },
            new() { Id = 13, StartTime = new DateTime(2026, 9, 29, 9, 0, 0), DurationSec = 3600 }, // nothing planned near
        ];
        var links = Compliance.Match(rides, plan);
        Assert.Equal(1, links[10]);
        Assert.Equal(2, links[11]);
        Assert.Equal(3, links[12]);
        Assert.False(links.ContainsKey(13));
    }

    [Fact]
    public void Manual_links_are_kept()
    {
        List<PlannedWorkout> plan = [new() { Id = 1, Date = new(2026, 9, 22) }, new() { Id = 2, Date = new(2026, 9, 23) }];
        List<Activity> rides = [new() { Id = 10, StartTime = new DateTime(2026, 9, 22, 18, 0, 0), PlannedWorkoutId = 2 }];
        var links = Compliance.Match(rides, plan);
        Assert.Equal(2, links[10]);
        Assert.Single(links);
    }

    [Fact]
    public void Bad_week_is_three_missed_or_under_60_percent()
    {
        List<PlannedWorkout> week =
        [
            new() { Tss = 100, Status = WorkoutStatus.Done },
            new() { Tss = 100, Status = WorkoutStatus.Done },
            new() { Tss = 100, Status = WorkoutStatus.Done },
            new() { Tss = 100, Status = WorkoutStatus.Missed },
        ];
        Assert.False(Compliance.IsBadWeek(week, 300));
        Assert.True(Compliance.IsBadWeek(week, 230));
        foreach (var w in week.Take(2)) w.Status = WorkoutStatus.Missed;
        Assert.True(Compliance.IsBadWeek(week, 1000));
    }

    [Fact]
    public void Fatigue_guard_needs_three_days_below_minus_30()
    {
        var loads = Enumerable.Range(1, 3).Select(i => new DailyLoad { Date = Today.AddDays(-i), Tsb = -31 }).ToList();
        Assert.True(Compliance.FatigueGuard(loads, Today));
        loads[1].Tsb = -29;
        Assert.False(Compliance.FatigueGuard(loads, Today));
    }
}
