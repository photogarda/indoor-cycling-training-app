using Trainer.Core.Models;
using Trainer.Core.Workouts;

namespace Trainer.Core.Planning;

/// <summary>Chooses a template of a given kind, respecting the athlete level's time-in-zone limits.</summary>
public sealed class TemplatePicker
{
    private readonly List<(WorkoutTemplate T, WorkoutLoad Load)> _all;

    public TemplatePicker(IEnumerable<WorkoutTemplate> templates)
    {
        _all = templates.Select(t => (t, WorkoutMetrics.Analyze(t.Steps))).ToList();
    }

    public WorkoutTemplate? ByName(string name) =>
        _all.Select(x => x.T).FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Picks a template of <paramref name="kind"/>. <paramref name="progress"/> runs 0 → 1 through a phase and
    /// moves the choice from easier to harder variants; a negative value picks the easiest (recovery/taper).
    /// </summary>
    public WorkoutTemplate? Pick(WorkoutKind kind, AthleteLevel level, double progress, string? hint = null, int variety = 0)
    {
        var candidates = _all.Where(x => x.T.Kind == kind && x.T.MinLevel <= level && level <= x.T.MaxLevel).ToList();
        if (candidates.Count == 0) candidates = _all.Where(x => x.T.Kind == kind).ToList();
        if (candidates.Count == 0) return null;
        if (hint is not null)
        {
            var hinted = candidates.Where(x => x.T.Name.Contains(hint, StringComparison.OrdinalIgnoreCase)).ToList();
            if (hinted.Count > 0) candidates = hinted;
        }

        if (kind is WorkoutKind.Recovery or WorkoutKind.Endurance or WorkoutKind.EnduranceSurges or WorkoutKind.LongRide
            or WorkoutKind.Opener or WorkoutKind.RampTest)
        {
            // Volume rides: rotate for variety, built-ins first so a plain ride is the default.
            var ordered = candidates.OrderBy(x => x.T.Id).ToList();
            return ordered[Math.Abs(variety) % ordered.Count].T;
        }

        var thrMax = LevelRules.MaxThresholdMinutes(level) * 60;
        var vo2Max = LevelRules.MaxVo2Minutes(level) * 60;
        Func<WorkoutLoad, int> measure = kind switch
        {
            WorkoutKind.Tempo or WorkoutKind.LongTempo => l => l.TempoSec + l.SweetSpotSec,
            WorkoutKind.SweetSpot => l => l.SweetSpotSec + l.ThresholdSec,
            WorkoutKind.Threshold or WorkoutKind.ThresholdClimbs or WorkoutKind.OverUnder => l => l.ThresholdSec + l.Vo2Sec,
            _ => l => l.Vo2Sec,
        };
        var limit = kind switch
        {
            WorkoutKind.Tempo or WorkoutKind.LongTempo => (int)(thrMax * 2.5),
            WorkoutKind.SweetSpot => (int)(thrMax * 1.5),
            WorkoutKind.Threshold or WorkoutKind.ThresholdClimbs or WorkoutKind.OverUnder => thrMax,
            WorkoutKind.Sprint or WorkoutKind.AnaerobicStarts or WorkoutKind.RaceSim => int.MaxValue,
            _ => vo2Max,
        };

        var sorted = candidates.OrderBy(x => measure(x.Load)).ThenBy(x => x.Load.Tss).ToList();
        var eligible = sorted.Where(x => measure(x.Load) <= limit).ToList();
        if (eligible.Count == 0) return sorted[0].T;
        if (progress < 0) return eligible[0].T;
        var index = (int)Math.Round((0.25 + 0.75 * Math.Clamp(progress, 0, 1)) * (eligible.Count - 1));
        return eligible[index].T;
    }
}
