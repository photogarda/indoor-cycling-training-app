using Trainer.Core.Models;
using Trainer.Core.Training;

namespace Trainer.Core.Workouts;

/// <summary>Time a workout spends in the bands that the athlete-level limits apply to.</summary>
public record WorkoutLoad(int TotalSec, int TempoSec, int SweetSpotSec, int ThresholdSec, int Vo2Sec, double Tss, double If);

public static class WorkoutMetrics
{
    public static WorkoutLoad Analyze(IEnumerable<WorkoutStep> steps)
    {
        int tempo = 0, ss = 0, thr = 0, vo2 = 0, total = 0;
        foreach (var s in WorkoutStep.Flatten(steps))
        {
            total += s.DurationSec;
            if (s.Kind == StepKind.Free) { vo2 += s.DurationSec; continue; }
            if (s.Kind is StepKind.Warmup or StepKind.Cooldown or StepKind.Recovery or StepKind.Filler) continue;
            switch (Zones.Of(s.PowerMid))
            {
                case PowerZone.Z3Tempo: tempo += s.DurationSec; break;
                case PowerZone.SweetSpot: ss += s.DurationSec; break;
                case PowerZone.Z4Threshold: thr += s.DurationSec; break;
                case PowerZone.Z5Vo2Max or PowerZone.Z6Anaerobic or PowerZone.Z7Sprint: vo2 += s.DurationSec; break;
            }
        }
        var (tss, intensity, _) = LoadMath.ForSteps(steps);
        return new WorkoutLoad(total, tempo, ss, thr, vo2, tss, intensity);
    }
}
