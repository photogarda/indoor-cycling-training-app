using Microsoft.EntityFrameworkCore;
using Trainer.Core.Models;
using Trainer.Core.Planning;
using Trainer.Core.Training;
using Trainer.Core.Workouts;

namespace Trainer.Data.Services;

public record CalendarData(
    List<PlannedWorkout> Workouts,
    List<Race> Races,
    List<BlockedDay> BlockedDays,
    List<Activity> Activities);

public partial class TrainerService
{
    public CalendarData GetCalendar(DateOnly from, DateOnly to)
    {
        using var db = Db();
        var fromTime = from.ToDateTime(TimeOnly.MinValue);
        var toTime = to.AddDays(1).ToDateTime(TimeOnly.MinValue);
        return new CalendarData(
            db.PlannedWorkouts.AsNoTracking().Where(w => !w.Superseded && w.Date >= from && w.Date <= to).OrderBy(w => w.Date).ToList(),
            db.Races.AsNoTracking().Where(r => r.Date >= from && r.Date <= to).ToList(),
            db.BlockedDays.AsNoTracking().Where(b => b.Date >= from && b.Date <= to).ToList(),
            db.Activities.AsNoTracking().Where(a => a.StartTime >= fromTime && a.StartTime < toTime).OrderBy(a => a.StartTime).ToList());
    }

    public PlannedWorkout? GetWorkout(int id)
    {
        using var db = Db();
        return db.PlannedWorkouts.AsNoTracking().FirstOrDefault(w => w.Id == id);
    }

    /// <summary>Current workouts in a date range. Skipped ones are left out unless asked for.</summary>
    public List<PlannedWorkout> GetWorkouts(DateOnly from, DateOnly to, bool includeSkipped = false)
    {
        using var db = Db();
        return db.PlannedWorkouts.AsNoTracking()
            .Where(w => !w.Superseded && w.Date >= from && w.Date <= to)
            .AsEnumerable()
            .Where(w => includeSkipped || !w.IsSkipped)
            .OrderBy(w => w.Date).ToList();
    }

    /// <summary>Blocks a day (holiday, work trip, sick). The plan shifts around it.</summary>
    public void BlockDay(DateOnly date, string reason)
    {
        using (var db = Db())
        {
            if (db.BlockedDays.Any(b => b.Date == date)) return;
            db.BlockedDays.Add(new BlockedDay { Date = date, Reason = reason });
            // Locked workouts on that day give way too.
            foreach (var w in db.PlannedWorkouts.Where(w => !w.Superseded && w.Date == date && w.Date >= Today))
            {
                w.Superseded = true;
            }
            db.SaveChanges();
        }
        Refresh();
    }

    public void UnblockDay(DateOnly date)
    {
        using (var db = Db())
        {
            db.BlockedDays.Where(b => b.Date == date).ExecuteDelete();
        }
        Refresh();
    }

    /// <summary>Reason prefix of a day freed by moving its workout away.</summary>
    public const string MovedAwayPrefix = "Moved: ";

    /// <summary>
    /// Moves a workout (drag-and-drop or "Move to"). It is locked on its new day, and the day it came from becomes
    /// a rest day: you moved it because you can't train then, so nothing else is planned there. Moving it back
    /// to its original day undoes that.
    /// </summary>
    public void MoveWorkout(int id, DateOnly newDate)
    {
        if (newDate < Today) throw new TrainerValidationException("Workouts can only be moved to today or later.");
        using (var db = Db())
        {
            var w = db.PlannedWorkouts.First(x => x.Id == id);
            if (w.Date < Today) throw new TrainerValidationException("Past workouts can't be moved.");
            if (w.Date == newDate) return;

            // A day freed by an earlier move can take a workout again; a day you blocked yourself can't.
            var destBlock = db.BlockedDays.FirstOrDefault(b => b.Date == newDate);
            if (destBlock is not null)
            {
                if (!destBlock.Reason.StartsWith(MovedAwayPrefix, StringComparison.Ordinal))
                    throw new TrainerValidationException("That day is blocked.");
                db.BlockedDays.Remove(destBlock);
            }

            var from = w.Date;
            var undo = w.OriginalDate == newDate;
            w.OriginalDate = undo ? null : w.OriginalDate ?? from;
            w.Notes = undo ? null : $"Moved from {from:ddd d MMM}";
            w.Date = newDate;
            w.Locked = true;
            w.Status = WorkoutStatus.Planned;

            // Keep the day it left free, unless this move is an undo or something you placed is still there.
            var othersStay = db.PlannedWorkouts.Any(x => x.Id != id && !x.Superseded && x.Date == from && (x.Locked || x.Date < Today));
            if (!undo && !othersStay && !db.BlockedDays.Any(b => b.Date == from))
                db.BlockedDays.Add(new BlockedDay { Date = from, Reason = $"{MovedAwayPrefix}{w.Name} → {newDate:ddd d MMM}" });
            db.SaveChanges();
        }
        Refresh();
    }

    /// <summary>
    /// Removes a workout from the plan. The day stays a rest day; if a key session (or 40 %+ of the week's
    /// load) is removed, next week repeats this week's load instead of stepping up.
    /// </summary>
    public void SkipWorkout(int id)
    {
        using (var db = Db())
        {
            var w = db.PlannedWorkouts.First(x => x.Id == id);
            if (w.Date < Today) throw new TrainerValidationException("Past workouts can't be removed.");
            w.Status = WorkoutStatus.Skipped;
            w.Locked = true;
            w.Notes = "Removed by you";
            db.SaveChanges();
        }
        Refresh();
    }

    /// <summary>Undoes a removal: the plan engine fills the day again.</summary>
    public void RestoreWorkout(int id)
    {
        using (var db = Db())
        {
            var w = db.PlannedWorkouts.First(x => x.Id == id);
            if (!w.IsSkipped) return;
            w.Superseded = true;
            db.SaveChanges();
        }
        Refresh();
    }

    public void SetIndoor(int id, bool indoor)
    {
        using var db = Db();
        var w = db.PlannedWorkouts.First(x => x.Id == id);
        w.Indoor = indoor;
        w.Locked = true; // a hand edit: keep it through regenerates
        db.SaveChanges();
        OnChanged();
    }

    public void SetLocked(int id, bool locked)
    {
        using (var db = Db())
        {
            var w = db.PlannedWorkouts.First(x => x.Id == id);
            w.Locked = locked;
            db.SaveChanges();
        }
        if (!locked) Refresh();
        else OnChanged();
    }

    /// <summary>Swaps a workout for another template, sized to the same duration, and locks it.</summary>
    public void SwapTemplate(int id, int templateId)
    {
        using var db = Db();
        var w = db.PlannedWorkouts.First(x => x.Id == id);
        var t = db.WorkoutTemplates.AsNoTracking().First(x => x.Id == templateId);
        var steps = t.Kind == WorkoutKind.RampTest ? t.Steps : WorkoutSizer.Fit(t.Steps, w.DurationSec);
        var (tss, intensity, seconds) = LoadMath.ForSteps(steps);
        w.TemplateId = t.Id;
        w.Name = t.Name;
        w.Kind = t.Kind;
        w.IsKey = t.Kind.IsKey();
        w.Steps = steps;
        w.DurationSec = seconds;
        w.Tss = Math.Round(tss, 1);
        w.IntensityFactor = Math.Round(intensity, 2);
        w.Locked = true;
        db.SaveChanges();
        OnChanged();
    }

    /// <summary>Changes a workout's length (endurance filler stretches) and locks it.</summary>
    public void ResizeWorkout(int id, TimeSpan duration)
    {
        using var db = Db();
        var w = db.PlannedWorkouts.First(x => x.Id == id);
        var t = w.TemplateId is { } tid ? db.WorkoutTemplates.AsNoTracking().FirstOrDefault(x => x.Id == tid) : null;
        var steps = WorkoutSizer.Fit(t?.Steps ?? w.Steps, (int)duration.TotalSeconds);
        var (tss, intensity, seconds) = LoadMath.ForSteps(steps);
        w.Steps = steps;
        w.DurationSec = seconds;
        w.Tss = Math.Round(tss, 1);
        w.IntensityFactor = Math.Round(intensity, 2);
        w.Locked = true;
        db.SaveChanges();
        OnChanged();
    }
}
