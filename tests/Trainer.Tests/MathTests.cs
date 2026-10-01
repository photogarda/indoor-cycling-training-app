using Trainer.Core.Models;
using Trainer.Core.Training;

namespace Trainer.Tests;

public class MathTests
{
    [Fact]
    public void NormalizedPower_of_steady_ride_equals_its_power()
    {
        var watts = Enumerable.Repeat(200.0, 3600).ToList();
        Assert.Equal(200, LoadMath.NormalizedPower(watts)!.Value, 6);
    }

    [Fact]
    public void NormalizedPower_weights_variable_riding_above_average()
    {
        var watts = Enumerable.Range(0, 3600).Select(i => i / 60 % 2 == 0 ? 300.0 : 100.0).ToList();
        var np = LoadMath.NormalizedPower(watts)!.Value;
        Assert.True(np > watts.Average() + 10, $"NP {np} should exceed average {watts.Average()}");
    }

    [Fact]
    public void One_hour_at_ftp_is_100_tss() => Assert.Equal(100, LoadMath.Tss(3600, 250, 250), 6);

    [Fact]
    public void Two_hours_at_75_percent_is_112_tss() => Assert.Equal(112.5, LoadMath.Tss(7200, 187.5, 250), 6);

    [Fact]
    public void HrTss_one_hour_at_threshold_hr_is_100() => Assert.Equal(100, LoadMath.HrTss(3600, 170, 170), 6);

    [Fact]
    public void Steps_at_ftp_for_an_hour_give_tss_100_if_1()
    {
        var (tss, intensity, seconds) = LoadMath.ForSteps([WorkoutStep.Make(StepKind.Work, 3600, 1.0)]);
        Assert.Equal(3600, seconds);
        Assert.Equal(100, tss, 3);
        Assert.Equal(1.0, intensity, 3);
    }

    [Fact]
    public void Pmc_follows_the_42_and_7_day_formulas()
    {
        var start = new DateOnly(2026, 1, 1);
        var tss = Enumerable.Range(0, 42).ToDictionary(i => start.AddDays(i), _ => 100.0);
        var pmc = Pmc.Compute(tss, start, start.AddDays(41));
        var expectedCtl = 100 * (1 - Math.Pow(41.0 / 42, 42));
        var expectedAtl = 100 * (1 - Math.Pow(6.0 / 7, 42));
        Assert.Equal(expectedCtl, pmc[^1].Ctl, 6);
        Assert.Equal(expectedAtl, pmc[^1].Atl, 6);
        // TSB is form going into the day: yesterday's CTL minus yesterday's ATL.
        Assert.Equal(pmc[^2].Ctl - pmc[^2].Atl, pmc[^1].Tsb, 6);
        Assert.Equal(0, pmc[0].Tsb);
    }

    [Fact]
    public void Ramp_cap_matches_simulated_ctl_rise()
    {
        var ctl = 50.0;
        var t = Pmc.MaxDailyTssForRamp(ctl, 5);
        var after = Pmc.Project(ctl, Enumerable.Repeat(t, 7));
        Assert.Equal(55, after, 6);
    }

    [Fact]
    public void Power_curve_finds_best_efforts()
    {
        var watts = Enumerable.Repeat(150.0, 3600).ToList();
        for (var i = 1000; i < 1300; i++) watts[i] = 320; // 5 min at 320
        for (var i = 2000; i < 2005; i++) watts[i] = 900; // 5 s sprint
        var curve = PowerCurve.Compute(watts);
        Assert.Equal(900, curve[5]);
        Assert.Equal(320, curve[300]);
        Assert.Equal(watts.Average(), curve[3600], 1);
        Assert.False(curve.ContainsKey(7200));
    }

    [Theory]
    [InlineData(0.50, PowerZone.Z1Recovery)]
    [InlineData(0.65, PowerZone.Z2Endurance)]
    [InlineData(0.82, PowerZone.Z3Tempo)]
    [InlineData(0.90, PowerZone.SweetSpot)]
    [InlineData(1.00, PowerZone.Z4Threshold)]
    [InlineData(1.12, PowerZone.Z5Vo2Max)]
    [InlineData(1.30, PowerZone.Z6Anaerobic)]
    [InlineData(2.00, PowerZone.Z7Sprint)]
    public void Zones_by_percent_ftp(double fraction, PowerZone zone) => Assert.Equal(zone, Zones.Of(fraction));

    [Fact]
    public void Ftp_on_a_date_uses_latest_entry_before_it()
    {
        List<FtpEntry> h = [new() { Date = new(2026, 1, 1), Watts = 240 }, new() { Date = new(2026, 3, 1), Watts = 255 }];
        Assert.Equal(240, Ftp.On(h, new(2026, 2, 15)));
        Assert.Equal(255, Ftp.On(h, new(2026, 3, 1)));
        Assert.Equal(240, Ftp.On(h, new(2025, 12, 1)));
    }

    [Fact]
    public void Ftp_estimate_is_95_percent_of_best_20_min_in_6_weeks()
    {
        var today = new DateOnly(2026, 10, 1);
        List<Activity> acts =
        [
            new() { StartTime = new DateTime(2026, 9, 20, 9, 0, 0), PowerCurve = new() { [1200] = 260 } },
            new() { StartTime = new DateTime(2026, 9, 25, 9, 0, 0), PowerCurve = new() { [1200] = 244 } },
            new() { StartTime = new DateTime(2026, 7, 1, 9, 0, 0), PowerCurve = new() { [1200] = 320 } }, // too old
        ];
        Assert.Equal(247, Ftp.Estimate(acts, today));
    }

    [Fact]
    public void Ride_summary_uses_power_and_falls_back_to_hr()
    {
        var t0 = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
        var ride = new RideData
        {
            StartTime = t0,
            Samples = Enumerable.Range(0, 3600).Select(i => new RideSample(t0.AddSeconds(i), 250, 150, 90)).ToList(),
        };
        var s = RideAnalyzer.Summarize(ride, 250, 165);
        Assert.Equal(100, s.Tss, 0);
        Assert.Equal(1.0, s.IntensityFactor!.Value, 3);
        Assert.False(s.HrBasedTss);

        var hrOnly = new RideData
        {
            StartTime = t0,
            Samples = Enumerable.Range(0, 3600).Select(i => new RideSample(t0.AddSeconds(i), null, 165, null)).ToList(),
        };
        var h = RideAnalyzer.Summarize(hrOnly, 250, 165);
        Assert.True(h.HrBasedTss);
        Assert.Equal(100, h.Tss, 0);
    }

    [Fact]
    public void Ride_summary_skips_long_pauses_and_fills_short_dropouts()
    {
        var t0 = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
        var samples = new List<RideSample>();
        for (var i = 0; i < 600; i += 2) samples.Add(new RideSample(t0.AddSeconds(i), 200, null, null)); // 0.5 Hz recording
        for (var i = 0; i < 600; i++) samples.Add(new RideSample(t0.AddSeconds(1800 + i), 200, null, null)); // after a 20-min stop
        var s = RideAnalyzer.Summarize(new RideData { StartTime = t0, Samples = samples }, 200, null);
        Assert.InRange(s.DurationSec, 1199, 1201);
        Assert.Equal(200, s.AvgPower);
    }
}
