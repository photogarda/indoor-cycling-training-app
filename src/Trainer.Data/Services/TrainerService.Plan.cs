using Microsoft.EntityFrameworkCore;
using Trainer.Core.Adaptation;
using Trainer.Core.Models;
using Trainer.Core.Planning;
using Trainer.Core.Training;

namespace Trainer.Data.Services;

public record PlanOverview(Plan? Plan, List<PlanWeek> Weeks, List<Race> Races);

public record WeekCompliance(DateOnly WeekStart, double PlannedTss, double ActualTss, int Planned, int Done, int Partial, int Missed);

public partial class TrainerService
{
    /// <summary>How many old plan versions to keep for history.</summary>
    public int PlanVersionsKept { get; set; } = 20;

    /// <summary>
    /// The adaptation loop: link rides to workouts and set done / partial / missed, recompute CTL/ATL/TSB,
    /// then regenerate from today forward around frozen (past and locked) workouts.
    /// </summary>
    public PlanResult Refresh()
    {
        var today = Today;
        UpdateCompliance(today);
        RecomputeLoads(today);
        var result = Regenerate(today);
        OnChanged();
        return result;
    }

    public void UpdateCompliance(DateOnly today)
    {
        using var db = Db();
        var from = today.AddDays(-60);
        var workouts = db.PlannedWorkouts.Where(w => !w.Superseded && w.Date >= from && w.Date <= today).ToList();
        var fromTime = from.AddDays(-1).ToDateTime(TimeOnly.MinValue);
        var activities = db.Activities.Where(a => a.StartTime >= fromTime).ToList();

        // Drop links that point at workouts that no longer exist in the current plan.
        var live = db.PlannedWorkouts.Where(w => !w.Superseded).Select(w => w.Id).ToHashSet();
        foreach (var a in activities.Where(a => a.PlannedWorkoutId is { } id && !live.Contains(id))) a.PlannedWorkoutId = null;

        var links = Compliance.Match(activities, workouts);
        foreach (var a in activities)
            if (links.TryGetValue(a.Id, out var wid)) a.PlannedWorkoutId = wid;

        var actualByWorkout = activities.Where(a => a.PlannedWorkoutId is not null)
            .GroupBy(a => a.PlannedWorkoutId!.Value).ToDictionary(g => g.Key, g => g.Sum(a => a.Tss));
        foreach (var w in workouts)
        {
            double? actual = actualByWorkout.TryGetValue(w.Id, out var t) ? t : null;
            w.Status = Compliance.Evaluate(w.Tss, actual, w.Date, today);
        }
        db.SaveChanges();
    }

    public void RecomputeLoads(DateOnly today)
    {
        using var db = Db();
        var acts = db.Activities.AsNoTracking().Select(a => new { a.StartTime, a.Tss }).ToList();
        db.DailyLoads.ExecuteDelete();
        if (acts.Count == 0) return;
        var tss = acts.GroupBy(a => DateOnly.FromDateTime(a.StartTime)).ToDictionary(g => g.Key, g => g.Sum(a => a.Tss));
        var from = tss.Keys.Min();
        var to = today > tss.Keys.Max() ? today : tss.Keys.Max();
        db.DailyLoads.AddRange(Pmc.Compute(tss, from, to));
        db.SaveChanges();
    }

    public PlanResult Regenerate(DateOnly today)
    {
        using var db = Db();
        var athlete = db.Athletes.AsNoTracking().First();
        var existing = db.PlannedWorkouts.Where(w => !w.Superseded).ToList();

        // Today's ride already done? Then today is frozen and planning starts tomorrow.
        var start = existing.Any(w => w.Date == today && w.Status is WorkoutStatus.Done or WorkoutStatus.Partial)
            ? today.AddDays(1) : today;

        var races = db.Races.AsNoTracking().ToList();
        var target = races.Where(r => r.Priority == RacePriority.A && r.Date >= start).MinBy(r => r.Date);
        DateOnly? seasonStart = target is null ? null
            : db.Plans.Where(p => p.TargetRaceId == target.Id).Select(p => (DateOnly?)p.StartDate).Min();

        var input = new PlanInput
        {
            Athlete = athlete,
            Start = start,
            Races = races,
            BlockedDays = db.BlockedDays.AsNoTracking().ToList(),
            FtpHistory = db.FtpHistory.AsNoTracking().ToList(),
            Templates = db.WorkoutTemplates.AsNoTracking().ToList(),
            Existing = existing,
            Loads = db.DailyLoads.AsNoTracking().Where(l => l.Date < start && l.Date >= start.AddDays(-7)).ToList(),
            BadWeeks = BadWeeks(db, start),
            SeasonStart = seasonStart ?? start,
        };
        var result = PlanEngine.Generate(input);

        using var tx = db.Database.BeginTransaction();
        foreach (var w in existing.Where(w => w.Date >= start && !w.Locked)) w.Superseded = true;
        foreach (var p in db.Plans.Where(p => p.IsActive)) p.IsActive = false;
        var plan = new Plan
        {
            CreatedAt = DateTime.Now,
            StartDate = start,
            EndDate = result.End,
            TargetRaceId = result.TargetRaceId,
            IsActive = true,
            Weeks = result.Weeks,
        };
        db.Plans.Add(plan);
        db.SaveChanges();
        foreach (var w in result.Workouts) w.PlanId = plan.Id;
        db.PlannedWorkouts.AddRange(result.Workouts);
        db.SaveChanges();
        PruneOldPlans(db);
        tx.Commit();

        LastPlanNotes = result.Notes;
        return result;
    }

    private void PruneOldPlans(TrainerDbContext db)
    {
        var keep = db.Plans.OrderByDescending(p => p.Id).Take(PlanVersionsKept).Select(p => p.Id).ToList();
        if (keep.Count < PlanVersionsKept) return;
        var oldest = keep.Min();
        db.PlannedWorkouts.Where(w => w.Superseded && w.PlanId < oldest).ExecuteDelete();
        db.Plans.Where(p => p.Id < oldest).ExecuteDelete();
    }

    private static HashSet<DateOnly> BadWeeks(TrainerDbContext db, DateOnly start)
    {
        var result = new HashSet<DateOnly>();
        var thisWeek = PlanEngine.WeekStart(start);
        for (var w = thisWeek.AddDays(-14); w < thisWeek; w = w.AddDays(7))
        {
            var end = w.AddDays(6);
            var workouts = db.PlannedWorkouts.AsNoTracking().Where(x => !x.Superseded && x.Date >= w && x.Date <= end).ToList();
            var actual = db.DailyLoads.AsNoTracking().Where(l => l.Date >= w && l.Date <= end).Sum(l => (double?)l.Tss) ?? 0;
            if (Compliance.IsBadWeek(workouts, actual)) result.Add(w);
        }
        return result;
    }

    // ---------- Read models for the screens ----------

    public PlanOverview GetPlanOverview()
    {
        using var db = Db();
        var plan = db.Plans.AsNoTracking().Include(p => p.Weeks).FirstOrDefault(p => p.IsActive);
        var weeks = plan?.Weeks.OrderBy(w => w.WeekStart).ToList() ?? [];
        return new PlanOverview(plan, weeks, db.Races.AsNoTracking().OrderBy(r => r.Date).ToList());
    }

    public List<DailyLoad> GetLoads(DateOnly? from = null)
    {
        using var db = Db();
        var q = db.DailyLoads.AsNoTracking();
        if (from is { } f) q = q.Where(l => l.Date >= f);
        return q.OrderBy(l => l.Date).ToList();
    }

    public List<WeekCompliance> GetCompliance(int weeks = 12)
    {
        using var db = Db();
        var thisWeek = PlanEngine.WeekStart(Today);
        var from = thisWeek.AddDays(-7 * (weeks - 1));
        var workouts = db.PlannedWorkouts.AsNoTracking().Where(w => !w.Superseded && w.Date >= from && w.Date <= Today).ToList();
        var loads = db.DailyLoads.AsNoTracking().Where(l => l.Date >= from).ToList();
        var list = new List<WeekCompliance>();
        for (var w = from; w <= thisWeek; w = w.AddDays(7))
        {
            var end = w.AddDays(6);
            var ws = workouts.Where(x => x.Date >= w && x.Date <= end).ToList();
            list.Add(new WeekCompliance(w, ws.Sum(x => x.Tss), loads.Where(l => l.Date >= w && l.Date <= end).Sum(l => l.Tss),
                ws.Count, ws.Count(x => x.Status == WorkoutStatus.Done), ws.Count(x => x.Status == WorkoutStatus.Partial),
                ws.Count(x => x.Status == WorkoutStatus.Missed)));
        }
        return list;
    }

    /// <summary>Power curve: best of the last 6 weeks and all time.</summary>
    public (Dictionary<int, double> SixWeeks, Dictionary<int, double> AllTime) GetPowerCurves()
    {
        using var db = Db();
        var all = db.Activities.AsNoTracking().Select(a => new { a.StartTime, a.PowerCurve }).ToList();
        var cutoff = Today.AddDays(-42).ToDateTime(TimeOnly.MinValue);
        return (PowerCurve.Merge(all.Where(a => a.StartTime >= cutoff).Select(a => a.PowerCurve)),
                PowerCurve.Merge(all.Select(a => a.PowerCurve)));
    }
}
