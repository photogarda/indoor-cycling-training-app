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
}
