using Dynastream.Fit;
using Trainer.Core.Models;
using StepKind = Trainer.Core.Models.StepKind;

namespace Trainer.Integrations.Fit;

/// <summary>
/// Writes a planned workout as a FIT workout file. Power targets are absolute watts (FIT custom power
/// values above 1000 mean watts + 1000), worked out from the FTP on the workout's date. The Edge runs it
/// and drives the trainer in ERG.
/// </summary>
public static class FitWorkoutWriter
{
    public static byte[] Write(PlannedWorkout workout, int ftp)
    {
        using var ms = new MemoryStream();
        Write(workout.Name, workout.Steps, ftp, workout.Indoor, ms, workout.Notes);
        return ms.ToArray();
    }

    public static void Write(string name, IReadOnlyList<WorkoutStep> steps, int ftp, bool indoor, Stream output, string? description = null)
    {
        if (ftp <= 0) throw new InvalidOperationException("Set your FTP before exporting workouts.");
        var fitSteps = new List<WorkoutStepMesg>();
        AddSteps(steps, ftp, fitSteps);
        if (fitSteps.Count == 0) throw new InvalidOperationException("The workout has no steps.");

        var encoder = new Encode(ProtocolVersion.V20);
        encoder.Open(output);

        var fileId = new FileIdMesg();
        fileId.SetType(Dynastream.Fit.File.Workout);
        fileId.SetManufacturer(Manufacturer.Development);
        fileId.SetProduct(1);
        fileId.SetSerialNumber((uint)Math.Abs(name.GetHashCode()) | 1);
        fileId.SetTimeCreated(new Dynastream.Fit.DateTime(System.DateTime.UtcNow));
        encoder.Write(fileId);

        var wkt = new WorkoutMesg();
        wkt.SetWktName(Truncate(name, 40));
        wkt.SetSport(Sport.Cycling);
        wkt.SetSubSport(indoor ? SubSport.IndoorCycling : SubSport.Road);
        wkt.SetNumValidSteps((ushort)fitSteps.Count);
        if (!string.IsNullOrWhiteSpace(description)) wkt.SetWktDescription(Truncate(description, 120));
        encoder.Write(wkt);

        foreach (var s in fitSteps) encoder.Write(s);
        encoder.Close();
    }

    private static void AddSteps(IEnumerable<WorkoutStep> steps, int ftp, List<WorkoutStepMesg> output)
    {
        foreach (var s in steps)
        {
            if (s.Kind == StepKind.Repeat)
            {
                var first = output.Count;
                AddSteps(s.Steps ?? [], ftp, output);
                if (output.Count == first) continue;
                var rep = new WorkoutStepMesg();
                rep.SetMessageIndex((ushort)output.Count);
                // Every step carries a name: the SDK encoder garbles names when some steps have one and others don't.
                rep.SetWktStepName($"Repeat x{s.Repeat}");
                rep.SetDurationType(WktStepDuration.RepeatUntilStepsCmplt);
                rep.SetDurationStep((uint)first);
                rep.SetRepeatSteps((uint)s.Repeat);
                rep.SetTargetType(WktStepTarget.Open);
                output.Add(rep);
                continue;
            }

            var m = new WorkoutStepMesg();
            m.SetMessageIndex((ushort)output.Count);
            m.SetWktStepName(Truncate(s.Label ?? DefaultLabel(s.Kind, s.PowerMid), 30));
            m.SetDurationType(WktStepDuration.Time);
            m.SetDurationTime(s.DurationSec);
            m.SetIntensity(s.Kind switch
            {
                StepKind.Warmup => Intensity.Warmup,
                StepKind.Cooldown => Intensity.Cooldown,
                StepKind.Recovery => Intensity.Recovery,
                _ => Intensity.Active,
            });
            if (s.Kind == StepKind.Free || s.PowerHigh <= 0)
            {
                m.SetTargetType(WktStepTarget.Open);
            }
            else
            {
                var (low, high) = Watts(s, ftp);
                m.SetTargetType(WktStepTarget.Power);
                m.SetTargetValue(0); // 0 = custom range below
                m.SetCustomTargetPowerLow((uint)(low + 1000));
                m.SetCustomTargetPowerHigh((uint)(high + 1000));
            }
            output.Add(m);
        }
    }

    /// <summary>Watts for a step. Single-value steps get a ±2 % band so the Edge shows a sensible range.</summary>
    public static (int Low, int High) Watts(WorkoutStep s, int ftp)
    {
        var low = s.PowerLow;
        var high = s.PowerHigh;
        if (Math.Abs(high - low) < 0.005)
        {
            low -= 0.02;
            high += 0.02;
        }
        return ((int)Math.Round(low * ftp), (int)Math.Round(high * ftp));
    }

    private static string DefaultLabel(StepKind k, double power) => k switch
    {
        StepKind.Warmup => "Warm up",
        StepKind.Cooldown => "Cool down",
        StepKind.Recovery => "Recover",
        StepKind.Filler => "Endurance",
        StepKind.Free => "ERG off",
        _ => power < 0.56 ? "Easy" : Trainer.Core.Training.Zones.Name(Trainer.Core.Training.Zones.Of(power)),
    };

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    /// <summary>A file name the Edge accepts: date plus name, letters and digits only.</summary>
    public static string FileName(PlannedWorkout w)
    {
        var clean = new string(w.Name.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray()).Trim('_');
        return $"{w.Date:yyyyMMdd}_{clean}.fit";
    }
}
