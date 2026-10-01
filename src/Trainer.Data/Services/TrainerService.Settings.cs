using Microsoft.EntityFrameworkCore;
using Trainer.Core.Models;
using Trainer.Core.Training;

namespace Trainer.Data.Services;

public partial class TrainerService
{
    public Athlete GetAthlete()
    {
        using var db = Db();
        return db.Athletes.AsNoTracking().First();
    }

    /// <summary>Saves athlete settings. Hours, days, level or goal changes regenerate the plan.</summary>
    public void SaveAthlete(Athlete athlete)
    {
        if (athlete.WeeklyHours is <= 0 or > 40) throw new TrainerValidationException("Weekly hours must be between 0 and 40.");
        if (athlete.TrainingDays.Count == 0) throw new TrainerValidationException("Pick at least one training day.");
        using (var db = Db())
        {
            var cur = db.Athletes.First();
            var replan = cur.WeeklyHours != athlete.WeeklyHours || !cur.TrainingDays.OrderBy(d => d).SequenceEqual(athlete.TrainingDays.OrderBy(d => d))
                         || cur.LongRideDay != athlete.LongRideDay || cur.Level != athlete.Level || cur.NoRaceGoal != athlete.NoRaceGoal;
            athlete.Id = cur.Id;
            athlete.PlanAnchor = cur.PlanAnchor;
            db.Entry(cur).CurrentValues.SetValues(athlete);
            cur.TrainingDays = [.. athlete.TrainingDays.Distinct().OrderBy(d => ((int)d + 6) % 7)];
            db.SaveChanges();
            if (!replan)
            {
                OnChanged();
                return;
            }
        }
        Refresh();
    }

    /// <summary>Integration settings (Edge path, Strava tokens) without triggering a regenerate.</summary>
    public void UpdateIntegrationSettings(Action<Athlete> update)
    {
        using var db = Db();
        update(db.Athletes.First());
        db.SaveChanges();
    }

    public List<FtpEntry> GetFtpHistory()
    {
        using var db = Db();
        return db.FtpHistory.AsNoTracking().OrderBy(f => f.Date).ToList();
    }

    public int CurrentFtp() => Ftp.On(GetFtpHistory(), Today);

    /// <summary>Records a new FTP. A new FTP regenerates the plan; future watts follow automatically.</summary>
    public void AddFtp(int watts, FtpMethod method, DateOnly? date = null)
    {
        if (watts is < 50 or > 700) throw new TrainerValidationException("FTP must be between 50 and 700 W.");
        using (var db = Db())
        {
            var d = date ?? Today;
            var same = db.FtpHistory.FirstOrDefault(f => f.Date == d);
            if (same is not null)
            {
                same.Watts = watts;
                same.Method = method;
            }
            else
            {
                db.FtpHistory.Add(new FtpEntry { Date = d, Watts = watts, Method = method });
            }
            db.SaveChanges();
        }
        Refresh();
    }

    public void DeleteFtp(int id)
    {
        using (var db = Db())
        {
            db.FtpHistory.Where(f => f.Id == id).ExecuteDelete();
        }
        Refresh();
    }

    /// <summary>95 % of the best 20 minutes in the last 6 weeks of imported rides.</summary>
    public int? EstimateFtp()
    {
        using var db = Db();
        var from = Today.AddDays(-43).ToDateTime(TimeOnly.MinValue);
        return Ftp.Estimate(db.Activities.AsNoTracking().Where(a => a.StartTime >= from).ToList(), Today);
    }
}
