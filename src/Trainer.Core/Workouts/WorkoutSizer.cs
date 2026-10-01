using Trainer.Core.Models;

namespace Trainer.Core.Workouts;

/// <summary>Sizes a template to a planned duration by stretching or shrinking its endurance filler.</summary>
public static class WorkoutSizer
{
    public const double FillerPower = 0.68;
    private const int MinFillerSec = 5 * 60;

    public static List<WorkoutStep> Fit(IEnumerable<WorkoutStep> template, int targetSec)
    {
        var steps = template.Select(s => s.Clone()).ToList();
        var total = steps.Sum(s => s.TotalSeconds);
        var delta = targetSec - total;
        var fillers = steps.Where(s => s.Kind == StepKind.Filler).ToList();

        if (fillers.Count == 0)
        {
            if (delta >= MinFillerSec)
            {
                // Insert endurance before the cooldown (or at the end).
                var at = steps.FindLastIndex(s => s.Kind == StepKind.Cooldown);
                if (at < 0) at = steps.Count;
                steps.Insert(at, WorkoutStep.Make(StepKind.Filler, RoundToMinute(delta), 0.65, 0.70, "endurance"));
            }
            return steps;
        }

        var fillerTotal = fillers.Sum(f => f.DurationSec);
        foreach (var f in fillers)
        {
            var share = fillerTotal > 0 ? (double)f.DurationSec / fillerTotal : 1.0 / fillers.Count;
            f.DurationSec = RoundToMinute(Math.Max(0, f.DurationSec + delta * share));
        }
        steps.RemoveAll(s => s.Kind == StepKind.Filler && s.DurationSec < MinFillerSec / 2);
        foreach (var f in steps.Where(s => s.Kind == StepKind.Filler && s.DurationSec < MinFillerSec))
            f.DurationSec = MinFillerSec;
        return steps;
    }

    private static int RoundToMinute(double seconds) => (int)(Math.Round(seconds / 60) * 60);
}
