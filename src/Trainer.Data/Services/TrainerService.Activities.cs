using Microsoft.EntityFrameworkCore;
using Trainer.Core.Models;
using Trainer.Core.Training;

namespace Trainer.Data.Services;

public enum ImportOutcome { Added, ReplacedStrava, Duplicate, Empty }

public record ImportResult(ImportOutcome Outcome, Activity? Activity);

public partial class TrainerService
{
    /// <summary>Same ride from two sources: start times within 2 minutes.</summary>
    public static readonly TimeSpan DuplicateWindow = TimeSpan.FromMinutes(2);

    public List<Activity> GetActivities(DateOnly? from = null, DateOnly? to = null)
    {
        using var db = Db();
        var q = db.Activities.AsNoTracking();
        if (from is { } f) q = q.Where(a => a.StartTime >= f.ToDateTime(TimeOnly.MinValue));
        if (to is { } t) q = q.Where(a => a.StartTime < t.AddDays(1).ToDateTime(TimeOnly.MinValue));
        return q.OrderByDescending(a => a.StartTime).ToList();
    }

    public bool HasActivityNear(DateTime start)
    {
        using var db = Db();
        var lo = start - DuplicateWindow;
        var hi = start + DuplicateWindow;
        return db.Activities.Any(a => a.StartTime >= lo && a.StartTime <= hi);
    }

    public bool HasStravaActivity(long stravaId)
    {
        using var db = Db();
        return db.Activities.Any(a => a.StravaId == stravaId);
    }

    /// <summary>
    /// Stores one ride. Duplicates (start ±2 min) are stored once; a FIT file from the Edge wins over Strava.
    /// Call <see cref="Refresh"/> after a batch (or use <see cref="ImportRides"/>).
    /// </summary>
    public ImportResult StoreRide(RideData ride, ActivitySource source)
    {
        if (ride.Samples.Count == 0 && ride.TimerSeconds is null or 0) return new(ImportOutcome.Empty, null);
        using var db = Db();
        var athlete = db.Athletes.AsNoTracking().First();
        var ftp = Ftp.On(db.FtpHistory.AsNoTracking().ToList(), DateOnly.FromDateTime(ride.StartTime));
        var s = RideAnalyzer.Summarize(ride, ftp, athlete.ThresholdHr);

        var lo = ride.StartTime - DuplicateWindow;
        var hi = ride.StartTime + DuplicateWindow;
        var dup = db.Activities.FirstOrDefault(a => a.StartTime >= lo && a.StartTime <= hi);
        if (dup is not null)
        {
            if (dup.Source == ActivitySource.Strava && source == ActivitySource.Fit)
            {
                Apply(dup, ride, s, source);
                dup.StravaId ??= ride.StravaId;
                db.SaveChanges();
                return new(ImportOutcome.ReplacedStrava, dup);
            }
            if (source == ActivitySource.Strava && dup.StravaId is null)
            {
                dup.StravaId = ride.StravaId;
                db.SaveChanges();
            }
            return new(ImportOutcome.Duplicate, dup);
        }

        var a = new Activity();
        Apply(a, ride, s, source);
        a.StravaId = ride.StravaId;
        db.Activities.Add(a);
        db.SaveChanges();
        return new(ImportOutcome.Added, a);
    }

    private static void Apply(Activity a, RideData ride, RideSummary s, ActivitySource source)
    {
        a.Name = ride.Name;
        a.StartTime = ride.StartTime;
        a.Source = source;
        a.DurationSec = s.DurationSec;
        a.DistanceKm = ride.DistanceKm is null ? null : Math.Round(ride.DistanceKm.Value, 2);
        a.AvgPower = s.AvgPower;
        a.NormalizedPower = s.NormalizedPower;
        a.IntensityFactor = s.IntensityFactor;
        a.Tss = s.Tss;
        a.HrBasedTss = s.HrBasedTss;
        a.AvgHr = s.AvgHr;
        a.AvgCadence = s.AvgCadence;
        a.PowerCurve = s.PowerCurve;
        if (ride.FilePath is not null) a.FilePath = ride.FilePath;
    }

    /// <summary>Stores a batch of rides, then links, recomputes load and regenerates once.</summary>
    public List<ImportResult> ImportRides(IEnumerable<(RideData Ride, ActivitySource Source)> rides)
    {
        var results = rides.Select(r => StoreRide(r.Ride, r.Source)).ToList();
        if (results.Any(r => r.Outcome is ImportOutcome.Added or ImportOutcome.ReplacedStrava)) Refresh();
        return results;
    }

    /// <summary>Links a ride to a planned workout by hand (or unlinks with null).</summary>
    public void LinkActivity(int activityId, int? plannedWorkoutId)
    {
        using (var db = Db())
        {
            var a = db.Activities.First(x => x.Id == activityId);
            if (plannedWorkoutId is { } pid)
                foreach (var other in db.Activities.Where(x => x.PlannedWorkoutId == pid && x.Id != activityId))
                    other.PlannedWorkoutId = null;
            if (a.PlannedWorkoutId is { } old && old != plannedWorkoutId)
            {
                var prev = db.PlannedWorkouts.FirstOrDefault(w => w.Id == old);
                if (prev is not null) prev.Status = WorkoutStatus.Planned; // re-evaluated in Refresh
            }
            a.PlannedWorkoutId = plannedWorkoutId;
            db.SaveChanges();
        }
        Refresh();
    }

    public void DeleteActivity(int id)
    {
        using (var db = Db())
        {
            var a = db.Activities.First(x => x.Id == id);
            if (a.PlannedWorkoutId is { } pid)
            {
                var w = db.PlannedWorkouts.FirstOrDefault(x => x.Id == pid);
                if (w is not null) w.Status = WorkoutStatus.Planned;
            }
            db.Activities.Remove(a);
            db.SaveChanges();
        }
        Refresh();
    }
}
