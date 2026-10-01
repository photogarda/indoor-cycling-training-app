using Trainer.Core.Models;
using Trainer.Core.Workouts;

namespace Trainer.Tests;

public class WorkoutTests
{
    [Fact]
    public void Parses_steps_repeats_and_labels()
    {
        var steps = IntervalNotation.Parse("wu 12m 50-70; 3x(10m 95-100 \"threshold\", 5m 55); fill 20m 65; cd 8m 50");
        Assert.Equal(4, steps.Count);
        Assert.Equal(StepKind.Warmup, steps[0].Kind);
        Assert.Equal(720, steps[0].DurationSec);
        Assert.Equal(0.5, steps[0].PowerLow, 3);
        Assert.Equal(0.7, steps[0].PowerHigh, 3);
        Assert.Equal(StepKind.Repeat, steps[1].Kind);
        Assert.Equal(3, steps[1].Repeat);
        Assert.Equal("threshold", steps[1].Steps![0].Label);
        Assert.Equal(StepKind.Filler, steps[2].Kind);
        Assert.Equal(12 * 60 + 45 * 60 + 20 * 60 + 8 * 60, steps.Sum(s => s.TotalSeconds));
    }

    [Fact]
    public void Parses_compound_durations_free_steps_and_nesting()
    {
        var steps = IntervalNotation.Parse("1h30m 65\n2x(3x(30s 120, 15s 50), 5m 50)\nfree 12s \"sprint\"");
        Assert.Equal(5400, steps[0].DurationSec);
        Assert.Equal(2 * (3 * 45 + 300), steps[1].TotalSeconds);
        Assert.Equal(StepKind.Free, steps[2].Kind);
        Assert.Equal(0, steps[2].PowerLow);
    }

    [Fact]
    public void Format_round_trips()
    {
        const string text = "wu 12m 50-70; 3x(10m 95-100 \"threshold\", 5m 55); free 20s \"start\"; fill 20m 65; cd 8m 50";
        var formatted = IntervalNotation.Format(IntervalNotation.Parse(text));
        Assert.Equal(text, formatted);
    }

    [Theory]
    [InlineData("")]
    [InlineData("10m")]
    [InlineData("3x(10m 95")]
    [InlineData("zz 10m 50")]
    [InlineData("10m 250 \"open")]
    [InlineData("10 95")]
    public void Rejects_bad_notation(string text)
    {
        Assert.False(IntervalNotation.TryParse(text, out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void Library_has_40_to_60_valid_templates_covering_every_kind()
    {
        var lib = WorkoutLibrary.BuiltIn();
        Assert.InRange(lib.Count, 40, 60);
        Assert.Equal(lib.Count, lib.Select(t => t.Name).Distinct().Count());
        foreach (var kind in Enum.GetValues<WorkoutKind>())
            Assert.Contains(lib, t => t.Kind == kind);
        foreach (var t in lib)
        {
            Assert.True(t.Steps.Sum(s => s.TotalSeconds) > 0, t.Name);
            Assert.All(WorkoutStep.Flatten(t.Steps), s => Assert.InRange(s.PowerHigh, 0, 2));
        }
    }

    [Fact]
    public void Sizer_stretches_filler_to_target()
    {
        var steps = IntervalNotation.Parse("wu 10m 50-65; fill 60m 65-72; cd 5m 50");
        var fitted = WorkoutSizer.Fit(steps, 150 * 60);
        Assert.Equal(150 * 60, fitted.Sum(s => s.TotalSeconds));
        Assert.Equal(135 * 60, fitted[1].DurationSec);
        // template untouched
        Assert.Equal(60 * 60, steps[1].DurationSec);
    }

    [Fact]
    public void Sizer_inserts_endurance_before_cooldown_when_template_has_no_filler()
    {
        var steps = IntervalNotation.Parse("wu 12m 50-70; 2x(20m 95-100, 5m 55); cd 8m 50");
        var fitted = WorkoutSizer.Fit(steps, 90 * 60);
        Assert.Equal(90 * 60, fitted.Sum(s => s.TotalSeconds));
        Assert.Equal(StepKind.Filler, fitted[^2].Kind);
        Assert.Equal(StepKind.Cooldown, fitted[^1].Kind);
    }

    [Fact]
    public void Sizer_leaves_template_longer_than_target_alone()
    {
        var steps = IntervalNotation.Parse("wu 12m 50-70; 4x(20m 95-100, 8m 55); cd 8m 50");
        var fitted = WorkoutSizer.Fit(steps, 60 * 60);
        Assert.Equal(steps.Sum(s => s.TotalSeconds), fitted.Sum(s => s.TotalSeconds));
    }

    [Fact]
    public void Metrics_count_time_in_threshold_and_vo2()
    {
        var load = WorkoutMetrics.Analyze(IntervalNotation.Parse("wu 10m 60; 2x(20m 95-100, 5m 55); 5x(3m 115, 3m 50); cd 5m 50"));
        Assert.Equal(40 * 60, load.ThresholdSec);
        Assert.Equal(15 * 60, load.Vo2Sec);
    }
}
