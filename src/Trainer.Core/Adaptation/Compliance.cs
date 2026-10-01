using Trainer.Core.Models;

namespace Trainer.Core.Adaptation;

/// <summary>Links rides to planned workouts and judges how well the plan was followed.</summary>
public static class Compliance
{
    /// <summary>Actual TSS at 80 %+ of plan is done, 50–80 % partial, under 50 % (or nothing by midnight) missed.</summary>
    public static WorkoutStatus Evaluate(double plannedTss, double? actualTss, DateOnly workoutDate, DateOnly today)
    {
        if (actualTss is null) return workoutDate < today ? WorkoutStatus.Missed : WorkoutStatus.Planned;
        if (plannedTss <= 0) return WorkoutStatus.Done;
        var ratio = actualTss.Value / plannedTss;
        return ratio >= 0.8 ? WorkoutStatus.Done : ratio >= 0.5 ? WorkoutStatus.Partial : WorkoutStatus.Missed;
    }

    /// <summary>
    /// Pairs activities with planned workouts on the same day, or failing that ±1 day. Links already set
    /// (for example by hand) are kept. Returns activity id → planned workout id.
    /// </summary>
    public static Dictionary<int, int> Match(IEnumerable<Activity> activities, IEnumerable<PlannedWorkout> workouts)
    {
        var plan = workouts.Where(w => !w.Superseded).ToList();
        var links = new Dictionary<int, int>();
        var taken = new HashSet<int>();
        var acts = activities.OrderBy(a => a.StartTime).ToList();

        foreach (var a in acts.Where(a => a.PlannedWorkoutId is not null))
        {
            links[a.Id] = a.PlannedWorkoutId!.Value;
            taken.Add(a.PlannedWorkoutId.Value);
        }

        // Same day first for every ride, then the day before, then the day after.
        foreach (var offset in new[] { 0, -1, 1 })
        {
            foreach (var a in acts.Where(a => !links.ContainsKey(a.Id)))
            {
                var match = plan
                    .Where(w => !taken.Contains(w.Id) && w.Date == a.Date.AddDays(offset))
                    .OrderByDescending(w => w.IsKey)
                    .ThenBy(w => Math.Abs(w.DurationSec - a.DurationSec))
                    .FirstOrDefault();
                if (match is null) continue;
                links[a.Id] = match.Id;
                taken.Add(match.Id);
            }
        }
        return links;
    }

    /// <summary>A bad week has 3+ missed workouts or under 60 % of planned TSS.</summary>
    public static bool IsBadWeek(IReadOnlyCollection<PlannedWorkout> weekWorkouts, double actualTss)
    {
        var planned = weekWorkouts.Where(w => !w.Superseded).ToList();
        if (planned.Count == 0) return false;
        var missed = planned.Count(w => w.Status == WorkoutStatus.Missed);
        var plannedTss = planned.Sum(w => w.Tss);
        return missed >= 3 || (plannedTss > 0 && actualTss < 0.6 * plannedTss);
    }

    /// <summary>True when TSB has been below −30 on each of the last 3 days before <paramref name="today"/>.</summary>
    public static bool FatigueGuard(IEnumerable<DailyLoad> loads, DateOnly today)
    {
        var recent = loads.Where(l => l.Date < today && l.Date >= today.AddDays(-3)).ToList();
        return recent.Count == 3 && recent.All(l => l.Tsb < -30);
    }
}
