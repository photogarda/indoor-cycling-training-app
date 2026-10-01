using Trainer.Core.Models;
using Trainer.Core.Planning;
using static Trainer.Tests.PlanTestKit;

namespace Trainer.Tests;

public class PlanEngineTests
{
    private static readonly DateOnly GranFondoDay = new(2027, 5, 16); // Sunday, 5 h → 2-week taper
    private static readonly DateOnly SpringA = new(2027, 3, 14); // Sunday
    private static readonly DateOnly SummerA = new(2027, 6, 27); // Sunday, 15 weeks later

    private static List<Phase> PhaseRun(PlanResult plan) => PhaseRun(plan.Weeks);

    private static List<Phase> PhaseRun(IEnumerable<PlanWeek> weeks) =>
        weeks.Select(w => w.Phase).Aggregate(new List<Phase>(), (acc, p) =>
        {
            if (acc.Count == 0 || acc[^1] != p) acc.Add(p);
            return acc;
        });

    private static int WeeksIn(PlanResult plan, Phase phase) => plan.Weeks.Count(w => w.Phase == phase);

    private static void AssertNoConsecutiveKeys(PlanResult plan)
    {
        var keys = plan.Workouts.Where(w => w.IsKey).Select(w => w.Date).OrderBy(d => d).ToList();
        for (var i = 1; i < keys.Count; i++)
            Assert.True(keys[i].DayNumber - keys[i - 1].DayNumber > 1, $"Keys on consecutive days: {keys[i - 1]} and {keys[i]}");
    }

    // ---------- One A race ----------

    [Fact]
    public void One_A_race_counts_phases_back_from_the_race()
    {
        var plan = Generate(Athlete(), [Race(1, GranFondoDay, RacePriority.A, EventType.GranFondo, 5)]);

        Assert.Equal([Phase.General, Phase.Base, Phase.Build, Phase.Specialty, Phase.Taper, Phase.Recovery], PhaseRun(plan));
        Assert.Equal(12, WeeksIn(plan, Phase.Base));
        Assert.Equal(8, WeeksIn(plan, Phase.Build));
        Assert.Equal(6, WeeksIn(plan, Phase.Specialty));
        Assert.Equal(2, WeeksIn(plan, Phase.Taper)); // race over 4 h
        Assert.Equal(1, plan.TargetRaceId);

        var raceWeek = plan.Weeks.Single(w => w.WeekStart == PlanEngine.WeekStart(GranFondoDay));
        Assert.Equal(Phase.Taper, raceWeek.Phase);
        Assert.Equal(1, raceWeek.RaceId);
        AssertNoConsecutiveKeys(plan);
    }

    [Fact]
    public void Race_day_is_free_with_openers_before_and_recovery_after()
    {
        var plan = Generate(Athlete(), [Race(1, GranFondoDay, RacePriority.A, EventType.GranFondo, 5)]);
        Assert.DoesNotContain(plan.Workouts, w => w.Date == GranFondoDay);
        Assert.Equal(WorkoutKind.Opener, plan.Workouts.Single(w => w.Date == GranFondoDay.AddDays(-1)).Kind);
        Assert.Equal(WorkoutKind.Recovery, plan.Workouts.Single(w => w.Date == GranFondoDay.AddDays(1)).Kind);
        // Nothing hard in the last two days before the race.
        Assert.DoesNotContain(plan.Workouts, w => w.IsKey && w.Date >= GranFondoDay.AddDays(-2) && w.Date < GranFondoDay);
    }

    [Fact]
    public void Short_race_gets_one_taper_week_and_taper_cuts_volume()
    {
        var plan = Generate(Athlete(), [Race(1, SpringA, RacePriority.A, EventType.Crit, 1, 8)]);
        Assert.Equal(1, WeeksIn(plan, Phase.Taper));
        var taper = plan.Weeks.Single(w => w.Phase == Phase.Taper);
        Assert.InRange(taper.TargetHours, 10 * 0.5, 10 * 0.6);
    }

    [Fact]
    public void Specialty_follows_the_event_type()
    {
        var plan = Generate(Athlete(), [Race(1, SpringA, RacePriority.A, EventType.Crit, 1, 8)]);
        var specialtyWeeks = plan.Weeks.Where(w => w.Phase == Phase.Specialty).Select(w => w.WeekStart).ToHashSet();
        var keys = plan.Workouts.Where(w => w.IsKey && specialtyWeeks.Contains(PlanEngine.WeekStart(w.Date))
                                            && w.Kind != WorkoutKind.RampTest).Select(w => w.Kind).ToHashSet();
        Assert.Contains(WorkoutKind.ThirtyThirty, keys);
        Assert.Contains(WorkoutKind.Anaerobic, keys);
        // Crit long ride is 2 h.
        var longRides = plan.Workouts.Where(w => w.Kind == WorkoutKind.LongRide && specialtyWeeks.Contains(PlanEngine.WeekStart(w.Date)));
        Assert.All(longRides, w => Assert.InRange(w.Duration.TotalHours, 1.9, 2.1));
    }

    [Fact]
    public void Short_build_up_shrinks_specialty_to_four_weeks()
    {
        var race = new DateOnly(2026, 12, 20); // ~12 weeks out
        var plan = Generate(Athlete(), [Race(1, race, RacePriority.A)]);
        Assert.Equal(4, WeeksIn(plan, Phase.Specialty));
        Assert.Equal(0, WeeksIn(plan, Phase.Base));
    }

    // ---------- Two A races ----------

    [Fact]
    public void Two_A_races_recover_then_rebuild_without_base_when_gap_is_under_16_weeks()
    {
        var plan = Generate(Athlete(), [Race(1, SpringA, RacePriority.A), Race(2, SummerA, RacePriority.A, EventType.Gravel, 6)]);
        var afterFirst = plan.Weeks.SkipWhile(w => w.WeekStart <= PlanEngine.WeekStart(SpringA)).ToList();
        Assert.Equal(Phase.Recovery, afterFirst[0].Phase);
        Assert.Equal([Phase.Recovery, Phase.Build, Phase.Specialty, Phase.Taper, Phase.Recovery],
            PhaseRun(afterFirst));
        Assert.Equal(2, afterFirst.Count(w => w.Phase == Phase.Taper)); // 6 h gravel
        AssertNoConsecutiveKeys(plan);
    }

    [Fact]
    public void A_races_closer_than_3_months_are_blocked()
    {
        var existing = new[] { Race(1, SpringA, RacePriority.A) };
        Assert.NotNull(RaceRules.Validate(Race(0, SpringA.AddMonths(2), RacePriority.A), existing));
        Assert.Null(RaceRules.Validate(Race(0, SpringA.AddMonths(3), RacePriority.A), existing));
        Assert.Null(RaceRules.Validate(Race(0, SpringA.AddMonths(1), RacePriority.B), existing));
        // Editing the race itself does not clash with its own old date.
        Assert.Null(RaceRules.Validate(Race(1, SpringA.AddDays(7), RacePriority.A), existing));
    }

    // ---------- A + B + C ----------

    [Fact]
    public void B_race_gets_mini_taper_opener_and_recovery_ride()
    {
        var bDay = new DateOnly(2027, 2, 21);
        var plan = Generate(Athlete(), [Race(1, SpringA, RacePriority.A, EventType.Crit, 1, 9), Race(2, bDay, RacePriority.B)]);
        Assert.DoesNotContain(plan.Workouts, w => w.Date == bDay);
        Assert.Equal(WorkoutKind.Opener, plan.Workouts.Single(w => w.Date == bDay.AddDays(-1)).Kind);
        Assert.Equal(WorkoutKind.Recovery, plan.Workouts.Single(w => w.Date == bDay.AddDays(1)).Kind);
        Assert.DoesNotContain(plan.Workouts, w => w.IsKey && w.Date >= bDay.AddDays(-3) && w.Date <= bDay.AddDays(1));
    }

    [Fact]
    public void C_race_has_no_taper_and_counts_as_the_weeks_key_workout()
    {
        var cDay = new DateOnly(2026, 11, 22); // Sunday in a load week
        var withC = Generate(Athlete(), [Race(1, SpringA, RacePriority.A), Race(3, cDay, RacePriority.C, EventType.Crit, 1)]);
        var without = Generate(Athlete(), [Race(1, SpringA, RacePriority.A)]);
        var week = PlanEngine.WeekStart(cDay);
        int Keys(PlanResult p) => p.Workouts.Count(w => w.IsKey && PlanEngine.WeekStart(w.Date) == week);

        Assert.DoesNotContain(withC.Workouts, w => w.Date == cDay);
        Assert.DoesNotContain(withC.Workouts, w => w.Kind == WorkoutKind.Opener && w.Date == cDay.AddDays(-1));
        Assert.Equal(Keys(without) - 1, Keys(withC));
        Assert.DoesNotContain(withC.Workouts, w => w.IsKey && Math.Abs(w.Date.DayNumber - cDay.DayNumber) == 1);
    }

    // ---------- No race ----------

    [Fact]
    public void No_race_rolls_8_week_blocks_at_least_8_weeks_ahead()
    {
        var plan = Generate(Athlete());
        Assert.All(plan.Weeks, w => Assert.Equal(Phase.General, w.Phase));
        Assert.True(plan.End >= Today.AddDays(55));
        Assert.True(plan.Weeks.Count >= 8);
        // 3 + 1: exactly one recovery week in any 4 consecutive weeks.
        for (var i = 0; i + 4 <= plan.Weeks.Count; i++)
            Assert.Equal(1, plan.Weeks.Skip(i).Take(4).Count(w => w.WeekType == WeekType.Recovery));
    }

    [Fact]
    public void No_race_ftp_goal_moves_from_sweet_spot_to_threshold_and_vo2()
    {
        var plan = Generate(Athlete(), start: new DateOnly(2026, 10, 5));
        var kinds = plan.Workouts.Where(w => w.IsKey && w.Kind != WorkoutKind.RampTest).Select(w => w.Kind).ToList();
        Assert.Contains(WorkoutKind.SweetSpot, kinds);
        Assert.Contains(WorkoutKind.Threshold, kinds);
        Assert.Contains(WorkoutKind.Vo2Max, kinds);
        Assert.True(kinds.IndexOf(WorkoutKind.SweetSpot) < kinds.IndexOf(WorkoutKind.Vo2Max));
    }

    [Fact]
    public void No_race_fitness_goal_stays_with_tempo_and_sweet_spot()
    {
        var a = Athlete();
        a.NoRaceGoal = NoRaceGoal.Fitness;
        var plan = Generate(a);
        var kinds = plan.Workouts.Where(w => w.IsKey && w.Kind != WorkoutKind.RampTest).Select(w => w.Kind).Distinct().ToList();
        Assert.All(kinds, k => Assert.Contains(k, new[] { WorkoutKind.Tempo, WorkoutKind.SweetSpot, WorkoutKind.LongTempo }));
    }

    [Fact]
    public void Basic_level_uses_2_plus_1_cycles()
    {
        var plan = Generate(Athlete(AthleteLevel.Basic));
        for (var i = 0; i + 3 <= plan.Weeks.Count; i++)
            Assert.Equal(1, plan.Weeks.Skip(i).Take(3).Count(w => w.WeekType == WeekType.Recovery));
    }

    // ---------- Splitting the week ----------

    [Fact]
    public void Ten_hours_over_four_days_splits_like_the_example()
    {
        // Monday start, a full load week with no ramp test due.
        var monday = new DateOnly(2026, 10, 19);
        var ftp = new List<FtpEntry> { new() { Date = new DateOnly(2026, 10, 13), Watts = 250, Method = FtpMethod.Ramp } };
        var plan = Generate(Athlete(), start: monday, ftp: ftp);
        var week = plan.Workouts.Where(w => w.Date < monday.AddDays(7)).ToList();

        Assert.Equal(Phase.General, plan.Weeks[0].Phase);
        Assert.Equal(WeekType.Load, plan.Weeks[0].WeekType);
        Assert.Equal([DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Saturday, DayOfWeek.Sunday], week.Select(w => w.Date.DayOfWeek));
        Assert.True(week[0].IsKey && week[1].IsKey);
        Assert.Equal(WorkoutKind.LongRide, week[2].Kind);
        Assert.False(week[3].IsKey);
        var hours = week.Sum(w => w.Duration.TotalHours);
        Assert.InRange(hours, plan.Weeks[0].TargetHours - 0.6, plan.Weeks[0].TargetHours + 0.6);
        Assert.InRange(week[2].Duration.TotalHours / plan.Weeks[0].TargetHours, 0.28, 0.40);
    }

    [Fact]
    public void Advanced_six_days_gets_three_keys_basic_gets_two()
    {
        DayOfWeek[] six = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Saturday, DayOfWeek.Sunday];
        var race = Race(1, SpringA, RacePriority.A, EventType.RoadRace, 3, 9);
        int MaxKeys(PlanResult p) => p.Workouts.Where(w => w.IsKey).GroupBy(w => PlanEngine.WeekStart(w.Date)).Max(g => g.Count());
        Assert.Equal(3, MaxKeys(Generate(Athlete(AthleteLevel.Advanced, 14, six), [race])));
        Assert.Equal(2, MaxKeys(Generate(Athlete(AthleteLevel.Amateur, 14, six), [race])));
        AssertNoConsecutiveKeys(Generate(Athlete(AthleteLevel.Pro, 16, six), [race]));
    }

    [Fact]
    public void Low_intensity_race_gets_one_key_a_week()
    {
        var plan = Generate(Athlete(), [Race(1, SpringA, RacePriority.A, EventType.GranFondo, 4, 2)]);
        var build = plan.Weeks.Where(w => w.Phase == Phase.Build).Select(w => w.WeekStart).ToHashSet();
        Assert.All(plan.Workouts.Where(w => w.IsKey && w.Kind != WorkoutKind.RampTest && build.Contains(PlanEngine.WeekStart(w.Date)))
            .GroupBy(w => PlanEngine.WeekStart(w.Date)), g => Assert.Single(g));
    }

    [Fact]
    public void Blocked_days_get_nothing_and_the_long_ride_moves()
    {
        var monday = new DateOnly(2026, 10, 19);
        var saturday = monday.AddDays(5);
        var plan = Generate(Athlete(), start: monday, blocked: [new BlockedDay { Date = saturday, Reason = "Wedding" }]);
        Assert.DoesNotContain(plan.Workouts, w => w.Date == saturday);
        var longRide = plan.Workouts.Single(w => w.Kind == WorkoutKind.LongRide && w.Date < monday.AddDays(7));
        Assert.Equal(DayOfWeek.Sunday, longRide.Date.DayOfWeek);
        AssertNoConsecutiveKeys(plan);
    }

    // ---------- Adaptation inputs ----------

    [Fact]
    public void Locked_workouts_are_kept_and_counted()
    {
        var monday = new DateOnly(2026, 10, 19);
        var locked = new PlannedWorkout
        {
            Id = 99, Date = monday.AddDays(2), Name = "My threshold", Kind = WorkoutKind.Threshold, IsKey = true,
            Locked = true, DurationSec = 5400, Tss = 100,
        };
        var plan = Generate(Athlete(), start: monday, existing: [locked]);
        Assert.DoesNotContain(plan.Workouts, w => w.Date == locked.Date);
        // Locked Wednesday key: Tuesday and Thursday can't be keys any more.
        Assert.DoesNotContain(plan.Workouts, w => w.IsKey && Math.Abs(w.Date.DayNumber - locked.Date.DayNumber) == 1);
    }

    [Fact]
    public void Missed_key_moves_to_the_next_free_non_adjacent_day()
    {
        var monday = new DateOnly(2026, 10, 19);
        var first = Generate(Athlete(), start: monday);
        var tuesdayKey = first.Workouts.First(w => w.Date == monday.AddDays(1));
        Assert.True(tuesdayKey.IsKey);
        tuesdayKey.Id = 1;
        tuesdayKey.Status = WorkoutStatus.Missed;
        var thursday = first.Workouts.First(w => w.Date == monday.AddDays(3));
        thursday.Id = 2;

        var wednesday = monday.AddDays(2);
        var plan = Generate(Athlete(), start: wednesday, existing: [tuesdayKey, thursday]);
        var moved = plan.Workouts.Where(w => w.Date < monday.AddDays(7) && w.Notes?.StartsWith("Moved") == true).ToList();
        Assert.Single(moved);
        Assert.Equal(tuesdayKey.Name, moved[0].Name);
        AssertNoConsecutiveKeys(plan);
    }

    [Fact]
    public void Bad_week_means_next_week_repeats_the_load()
    {
        var monday = new DateOnly(2026, 10, 19); // load week after a recovery week
        var normal = Generate(Athlete(), start: monday.AddDays(7));
        var repeat = Generate(Athlete(), start: monday.AddDays(7), badWeeks: new HashSet<DateOnly> { monday });
        var lastWeekHours = Generate(Athlete(), start: monday).Weeks[0].TargetHours;
        Assert.NotEqual(lastWeekHours, normal.Weeks[0].TargetHours);
        Assert.Equal(lastWeekHours, repeat.Weeks[0].TargetHours);
        Assert.Contains(repeat.Notes, n => n.Contains("repeats"));
    }

    [Fact]
    public void Fatigue_guard_swaps_the_next_key_for_endurance()
    {
        var start = new DateOnly(2026, 10, 19);
        var loads = Enumerable.Range(1, 3).Select(i => new DailyLoad { Date = start.AddDays(-i), Ctl = 60, Atl = 95, Tsb = -35 }).ToList();
        var normal = Generate(Athlete(), start: start, loads: loads.Select(l => new DailyLoad { Date = l.Date, Ctl = 60, Atl = 70, Tsb = -10 }).ToList());
        var tired = Generate(Athlete(), start: start, loads: loads);
        var firstKey = normal.Workouts.First(w => w.IsKey);
        var swapped = tired.Workouts.Single(w => w.Date == firstKey.Date);
        Assert.False(swapped.IsKey);
        Assert.Equal(WorkoutKind.Endurance, swapped.Kind);
        Assert.Contains(tired.Notes, n => n.Contains("Fatigue guard"));
    }

    [Fact]
    public void Ctl_ramp_is_capped_for_a_detrained_rider()
    {
        var start = new DateOnly(2026, 10, 19);
        var loads = new List<DailyLoad> { new() { Date = start.AddDays(-1), Ctl = 20, Atl = 20, Tsb = 0 } };
        var plan = Generate(Athlete(hours: 12), start: start, loads: loads);
        var first = plan.Weeks[0];
        Assert.True(first.PlannedCtl - 20 <= 5.5, $"CTL rose {first.PlannedCtl - 20:0.0}");
        Assert.Contains(plan.Notes, n => n.Contains("CTL rise"));
    }

    // ---------- FTP tests ----------

    [Fact]
    public void Ramp_test_follows_recovery_weeks_and_avoids_A_races()
    {
        var plan = Generate(Athlete(), [Race(1, GranFondoDay, RacePriority.A, EventType.GranFondo, 5)]);
        var tests = plan.Workouts.Where(w => w.Kind == WorkoutKind.RampTest).Select(w => w.Date).ToList();
        Assert.True(tests.Count >= 5);
        for (var i = 1; i < tests.Count; i++) Assert.True(tests[i].DayNumber - tests[i - 1].DayNumber >= 24);
        Assert.All(tests, d => Assert.True(Math.Abs(d.DayNumber - GranFondoDay.DayNumber) >= 10));
        foreach (var d in tests.Skip(1))
        {
            var prevWeek = plan.Weeks.Single(w => w.WeekStart == PlanEngine.WeekStart(d).AddDays(-7));
            Assert.Equal(WeekType.Recovery, prevWeek.WeekType);
        }
    }

    [Fact]
    public void Without_any_ftp_the_first_workout_is_a_ramp_test()
    {
        var plan = Generate(Athlete(), ftp: []);
        Assert.Equal(WorkoutKind.RampTest, plan.Workouts[0].Kind);
    }

    // ---------- Determinism and stability ----------

    [Fact]
    public void Same_input_gives_same_plan()
    {
        var races = new[] { Race(1, SpringA, RacePriority.A), Race(2, new DateOnly(2027, 2, 21), RacePriority.B) };
        var a = Describe(Generate(Athlete(), races));
        var b = Describe(Generate(Athlete(), races));
        Assert.Equal(a, b);
    }

    [Fact]
    public void Regenerating_later_keeps_phases_and_recovery_weeks_in_place()
    {
        var races = new[] { Race(1, GranFondoDay, RacePriority.A, EventType.GranFondo, 5) };
        var first = Generate(Athlete(), races);
        var later = Generate(Athlete(), races, start: Today.AddDays(45), seasonStart: Today);
        foreach (var w in later.Weeks)
        {
            var same = first.Weeks.Single(x => x.WeekStart == w.WeekStart);
            Assert.Equal(same.Phase, w.Phase);
            Assert.Equal(same.WeekType, w.WeekType);
        }
    }
}

