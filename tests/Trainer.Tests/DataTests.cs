using Trainer.Core.Models;
using Trainer.Core.Training;
using Trainer.Data.Services;

namespace Trainer.Tests;

public class DataTests
{
    private static RideData Ride(DateTime start, int minutes, double watts, string name = "Ride") => new()
    {
        Name = name,
        StartTime = start,
        Samples = Enumerable.Range(0, minutes * 60).Select(i => new RideSample(start.AddSeconds(i), watts, 140, 88)).ToList(),
    };

    [Fact]
    public void Settings_survive_a_restart()
    {
        using var t = new TestDb();
        var a = t.Service.GetAthlete();
        a.Name = "Kaspars";
        a.WeeklyHours = 10;
        a.Level = AthleteLevel.Advanced;
        a.TrainingDays = [DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Saturday, DayOfWeek.Sunday];
        a.LongRideDay = DayOfWeek.Sunday;
        a.EdgePath = @"E:\";
        t.Service.SaveAthlete(a);
        t.Service.AddFtp(285, FtpMethod.Manual);

        var restarted = t.Create();
        restarted.Initialize();
        var b = restarted.GetAthlete();
        Assert.Equal("Kaspars", b.Name);
        Assert.Equal(AthleteLevel.Advanced, b.Level);
        Assert.Equal([DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Saturday, DayOfWeek.Sunday], b.TrainingDays);
        Assert.Equal(DayOfWeek.Sunday, b.LongRideDay);
        Assert.Equal(@"E:\", b.EdgePath);
        Assert.Equal(285, restarted.CurrentFtp());
    }

    [Fact]
    public void Initialize_seeds_library_once()
    {
        using var t = new TestDb();
        var count = t.Service.GetTemplates().Count;
        Assert.InRange(count, 40, 60);
        t.Create().Initialize();
        Assert.Equal(count, t.Service.GetTemplates().Count);
    }

    [Fact]
    public void Close_A_races_are_refused()
    {
        using var t = new TestDb();
        t.Service.SaveRace(PlanTestKit.Race(0, new DateOnly(2027, 5, 16), RacePriority.A));
        Assert.Throws<TrainerValidationException>(() => t.Service.SaveRace(PlanTestKit.Race(0, new DateOnly(2027, 6, 20), RacePriority.A)));
        t.Service.SaveRace(PlanTestKit.Race(0, new DateOnly(2027, 6, 20), RacePriority.B));
        Assert.Equal(2, t.Service.GetRaces().Count);
    }

    [Fact]
    public void Refresh_builds_a_plan_and_keeps_old_versions()
    {
        using var t = new TestDb();
        t.Service.AddFtp(250, FtpMethod.Manual);
        t.Service.SaveRace(PlanTestKit.Race(0, new DateOnly(2027, 5, 16), RacePriority.A, EventType.GranFondo, 5));
        var overview = t.Service.GetPlanOverview();
        Assert.NotNull(overview.Plan);
        Assert.Contains(overview.Weeks, w => w.Phase == Phase.Taper);
        var first = t.Service.GetWorkouts(t.Today, t.Today.AddDays(13));
        Assert.NotEmpty(first);

        t.Service.Refresh();
        var again = t.Service.GetWorkouts(t.Today, t.Today.AddDays(13));
        Assert.Equal(first.Select(w => (w.Date, w.Name, w.DurationSec)), again.Select(w => (w.Date, w.Name, w.DurationSec)));
        Assert.All(again, w => Assert.DoesNotContain(first, f => f.Id == w.Id)); // new rows, old ones superseded
    }

    [Fact]
    public void Moving_a_workout_locks_it_and_reflows_the_week()
    {
        using var t = new TestDb();
        t.Service.AddFtp(250, FtpMethod.Manual);
        var week = t.Service.GetWorkouts(t.Today, t.Today.AddDays(6));
        var key = week.First(w => w.IsKey && w.Kind != WorkoutKind.RampTest);
        var target = t.Today.AddDays(5);
        while (week.Any(w => w.Date == target)) target = target.AddDays(1);

        t.Service.MoveWorkout(key.Id, target);
        var after = t.Service.GetWorkouts(t.Today, t.Today.AddDays(13));
        var moved = after.Single(w => w.Id == key.Id);
        Assert.True(moved.Locked);
        Assert.Equal(target, moved.Date);
        Assert.Single(after, w => w.Date == target);
        var keys = after.Where(w => w.IsKey).Select(w => w.Date).OrderBy(d => d).ToList();
        for (var i = 1; i < keys.Count; i++) Assert.True(keys[i].DayNumber - keys[i - 1].DayNumber > 1);
    }

    [Fact]
    public void Blocking_a_day_clears_it()
    {
        using var t = new TestDb();
        t.Service.AddFtp(250, FtpMethod.Manual);
        var day = t.Service.GetWorkouts(t.Today.AddDays(1), t.Today.AddDays(7)).First().Date;
        t.Service.BlockDay(day, "Work trip");
        Assert.DoesNotContain(t.Service.GetWorkouts(day, day), w => true);
        t.Service.UnblockDay(day);
        Assert.NotEmpty(t.Service.GetWorkouts(day, day));
    }

    [Fact]
    public void Imported_ride_links_to_plan_and_updates_load()
    {
        using var t = new TestDb();
        t.Service.AddFtp(250, FtpMethod.Manual);
        var planned = t.Service.GetWorkouts(t.Today, t.Today.AddDays(6)).First();
        // Next day: ride done as planned.
        t.Today = planned.Date.AddDays(1);
        var start = planned.Date.ToDateTime(new TimeOnly(18, 0));
        var minutes = planned.DurationSec / 60;
        var watts = planned.IntensityFactor * 250;
        var results = t.Service.ImportRides([(Ride(start, minutes, watts), ActivitySource.Fit)]);
        Assert.Equal(ImportOutcome.Added, results[0].Outcome);

        var act = t.Service.GetActivities().Single();
        Assert.Equal(planned.Id, act.PlannedWorkoutId);
        Assert.Equal(WorkoutStatus.Done, t.Service.GetWorkout(planned.Id)!.Status);
        var load = t.Service.GetLoads().Single(l => l.Date == planned.Date);
        Assert.Equal(act.Tss, load.Tss, 3);
        Assert.True(load.Ctl > 0);
    }

    [Fact]
    public void Same_ride_from_strava_and_fit_is_stored_once_and_fit_wins()
    {
        using var t = new TestDb();
        t.Service.AddFtp(250, FtpMethod.Manual);
        var start = new DateTime(2026, 9, 30, 7, 0, 0);
        var strava = Ride(start, 60, 180, "Morning ride");
        strava.StravaId = 42;
        t.Service.ImportRides([(strava, ActivitySource.Strava)]);
        var fit = Ride(start.AddSeconds(70), 60, 200, "Edge ride");
        fit.FilePath = "edge.fit";
        var r = t.Service.ImportRides([(fit, ActivitySource.Fit)]);
        Assert.Equal(ImportOutcome.ReplacedStrava, r[0].Outcome);

        var act = t.Service.GetActivities().Single();
        Assert.Equal(ActivitySource.Fit, act.Source);
        Assert.Equal(42, act.StravaId);
        Assert.Equal(200, act.AvgPower);

        var again = t.Service.ImportRides([(strava, ActivitySource.Strava)]);
        Assert.Equal(ImportOutcome.Duplicate, again[0].Outcome);
        Assert.Single(t.Service.GetActivities());
    }

    [Fact]
    public void Missed_rides_are_marked_by_midnight()
    {
        using var t = new TestDb();
        t.Service.AddFtp(250, FtpMethod.Manual);
        var planned = t.Service.GetWorkouts(t.Today, t.Today.AddDays(6)).First();
        t.Today = planned.Date.AddDays(2);
        t.Service.Refresh();
        Assert.Equal(WorkoutStatus.Missed, t.Service.GetWorkout(planned.Id)!.Status);
    }

    [Fact]
    public void Ftp_estimate_uses_recent_rides()
    {
        using var t = new TestDb();
        t.Service.AddFtp(250, FtpMethod.Manual);
        t.Service.ImportRides([(Ride(new DateTime(2026, 9, 20, 9, 0, 0), 30, 260), ActivitySource.Fit)]);
        Assert.Equal(247, t.Service.EstimateFtp());
    }

    [Fact]
    public void Custom_templates_can_be_added_but_built_ins_are_protected()
    {
        using var t = new TestDb();
        var builtIn = t.Service.GetTemplates().First();
        Assert.Throws<TrainerValidationException>(() => t.Service.DeleteTemplate(builtIn.Id));
        t.Service.SaveTemplate(new WorkoutTemplate
        {
            Name = "My 4×10", Kind = WorkoutKind.Threshold,
            Steps = Trainer.Core.Workouts.IntervalNotation.Parse("wu 10m 60; 4x(10m 98, 4m 55); cd 10m 50"),
        });
        var mine = t.Service.GetTemplates().Single(x => x.Name == "My 4×10");
        Assert.False(mine.BuiltIn);
        Assert.Equal(4, mine.Steps[1].Repeat);
        t.Service.DeleteTemplate(mine.Id);
    }

    [Fact]
    public void Ramp_test_ride_sets_ftp_and_replans()
    {
        using var t = new TestDb();
        t.Service.AddFtp(200, FtpMethod.Estimate); // a guess: the plan opens with a ramp test
        var test = t.Service.GetWorkouts(t.Today, t.Today.AddDays(6)).First();
        Assert.Equal(WorkoutKind.RampTest, test.Kind);

        // Ride the ramp: last full minute at 360 W.
        var start = test.Date.ToDateTime(new TimeOnly(18, 0));
        var samples = new List<RideSample>();
        for (var i = 0; i < 1500; i++) samples.Add(new RideSample(start.AddSeconds(i), i < 1300 ? 150 + i / 10.0 : (i < 1360 ? 360 : 80), 150, 90));
        t.Today = test.Date.AddDays(1);
        t.Service.ImportRides([(new RideData { Name = "Ramp", StartTime = start, Samples = samples }, ActivitySource.Fit)]);

        Assert.Equal(270, t.Service.CurrentFtp()); // 75 % of 360
        Assert.Contains(t.Service.GetFtpHistory(), f => f.Method == FtpMethod.Ramp && f.Date == test.Date);
        Assert.Contains(t.Service.LastPlanNotes, n => n.Contains("new FTP 270 W"));
        // Applied once only.
        t.Service.Refresh();
        Assert.Single(t.Service.GetFtpHistory(), f => f.Method == FtpMethod.Ramp);
    }

    [Fact]
    public void Backup_and_restore_round_trip()
    {
        using var t = new TestDb();
        t.Service.AddFtp(250, FtpMethod.Manual);
        t.Service.SaveRace(PlanTestKit.Race(0, new DateOnly(2027, 5, 16), RacePriority.A));
        File.WriteAllText(Path.Combine(t.Paths.FitFolder, "ride.fit"), "fit");
        var zip = Path.Combine(Path.GetTempPath(), $"trainer-backup-{Guid.NewGuid():N}.zip");
        try
        {
            BackupService.Backup(t.Paths, zip);
            t.Service.DeleteRace(t.Service.GetRaces().Single().Id);
            File.Delete(Path.Combine(t.Paths.FitFolder, "ride.fit"));
            Assert.Empty(t.Service.GetRaces());

            BackupService.Restore(t.Paths, zip);
            var restarted = t.Create();
            restarted.Initialize();
            Assert.Single(restarted.GetRaces());
            Assert.True(File.Exists(Path.Combine(t.Paths.FitFolder, "ride.fit")));
        }
        finally
        {
            File.Delete(zip);
        }
    }

    [Fact]
    public void Removing_a_workout_keeps_the_day_free_and_can_be_undone()
    {
        using var t = new TestDb();
        t.Service.AddFtp(250, FtpMethod.Manual);
        var w = t.Service.GetWorkouts(t.Today.AddDays(1), t.Today.AddDays(6)).First(x => x.IsKey);
        t.Service.SkipWorkout(w.Id);

        Assert.DoesNotContain(t.Service.GetWorkouts(w.Date, w.Date), x => true); // hidden from normal lists and exports
        var skipped = Assert.Single(t.Service.GetWorkouts(w.Date, w.Date, includeSkipped: true));
        Assert.Equal(WorkoutStatus.Skipped, skipped.Status);
        t.Service.Refresh();
        Assert.Equal(WorkoutStatus.Skipped, t.Service.GetWorkout(w.Id)!.Status); // survives regenerating

        t.Today = w.Date.AddDays(2); // compliance doesn't turn it into "missed"
        t.Service.Refresh();
        Assert.Equal(WorkoutStatus.Skipped, t.Service.GetWorkout(w.Id)!.Status);

        t.Today = PlanTestKit.Today;
        t.Service.RestoreWorkout(w.Id);
        Assert.NotEmpty(t.Service.GetWorkouts(w.Date, w.Date));
    }

    [Fact]
    public void Moving_to_another_week_remembers_the_original_day()
    {
        using var t = new TestDb();
        t.Service.AddFtp(250, FtpMethod.Manual);
        var w = t.Service.GetWorkouts(t.Today.AddDays(1), t.Today.AddDays(6)).First(x => x.IsKey);
        var target = Trainer.Core.Planning.PlanEngine.WeekStart(w.Date).AddDays(9);
        t.Service.MoveWorkout(w.Id, target);
        var moved = t.Service.GetWorkout(w.Id)!;
        Assert.Equal(target, moved.Date);
        Assert.Equal(w.Date, moved.OriginalDate);
        // Moving back into the original week forgets it again.
        t.Service.MoveWorkout(w.Id, w.Date);
        Assert.Null(t.Service.GetWorkout(w.Id)!.OriginalDate);
    }

    [Fact]
    public void Plan_period_is_saved_and_applied()
    {
        using var t = new TestDb();
        t.Service.AddFtp(250, FtpMethod.Manual);
        var from = t.Today.AddDays(14);
        var to = t.Today.AddDays(70);
        Assert.Throws<TrainerValidationException>(() => t.Service.SetPlanPeriod(from, from.AddDays(3)));
        t.Service.SetPlanPeriod(from, to);
        Assert.Equal(from, t.Service.GetAthlete().PlanStartDate);
        Assert.Empty(t.Service.GetWorkouts(t.Today, from.AddDays(-1)));
        Assert.Empty(t.Service.GetWorkouts(to.AddDays(1), to.AddDays(60)));
        Assert.NotEmpty(t.Service.GetWorkouts(from, to));
        t.Service.SetPlanPeriod(null, null); // back to automatic
        Assert.NotEmpty(t.Service.GetWorkouts(t.Today, from.AddDays(-1)));
    }

    [Fact]
    public void Moving_to_the_next_day_leaves_the_old_day_empty_and_undo_restores_it()
    {
        using var t = new TestDb();
        t.Service.AddFtp(250, FtpMethod.Manual);
        var week = t.Service.GetWorkouts(t.Today.AddDays(1), t.Today.AddDays(13));
        var w = week.First(x => x.IsKey && x.Kind != WorkoutKind.RampTest);
        var dayBefore = t.Service.GetWorkouts(w.Date.AddDays(1), w.Date.AddDays(1));

        t.Service.MoveWorkout(w.Id, w.Date.AddDays(1)); // "can't do it Monday, do it Tuesday"

        Assert.Empty(t.Service.GetWorkouts(w.Date, w.Date)); // Monday stays a rest day
        var blocked = t.Service.GetCalendar(w.Date, w.Date).BlockedDays.Single();
        Assert.StartsWith(TrainerService.MovedAwayPrefix, blocked.Reason);
        var tuesday = Assert.Single(t.Service.GetWorkouts(w.Date.AddDays(1), w.Date.AddDays(1)));
        Assert.Equal(w.Id, tuesday.Id); // only the moved workout on Tuesday
        // Whatever was planned on Tuesday was not pushed onto Monday.
        Assert.All(dayBefore, x => Assert.DoesNotContain(t.Service.GetWorkouts(w.Date, w.Date), y => y.Name == x.Name));

        // Moving it back undoes everything.
        t.Service.MoveWorkout(w.Id, w.Date);
        Assert.Empty(t.Service.GetCalendar(w.Date, w.Date.AddDays(1)).BlockedDays);
        Assert.Null(t.Service.GetWorkout(w.Id)!.OriginalDate);
        Assert.Contains(t.Service.GetWorkouts(w.Date, w.Date), x => x.Id == w.Id);
    }

    [Fact]
    public void A_day_you_blocked_yourself_still_refuses_moves()
    {
        using var t = new TestDb();
        t.Service.AddFtp(250, FtpMethod.Manual);
        var w = t.Service.GetWorkouts(t.Today.AddDays(1), t.Today.AddDays(13)).First();
        var target = w.Date.AddDays(2);
        t.Service.BlockDay(target, "Holiday");
        Assert.Throws<TrainerValidationException>(() => t.Service.MoveWorkout(w.Id, target));
    }
}
