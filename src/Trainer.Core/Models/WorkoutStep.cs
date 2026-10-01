namespace Trainer.Core.Models;

/// <summary>
/// One step of a structured workout. Power is stored as a fraction of FTP (0.88 = 88 %), never in watts;
/// watts are worked out at export time from the FTP on the workout's date.
/// A <see cref="StepKind.Repeat"/> step holds child steps and a repeat count.
/// </summary>
public class WorkoutStep
{
    public StepKind Kind { get; set; }
    public int DurationSec { get; set; }
    public double PowerLow { get; set; }
    public double PowerHigh { get; set; }
    public string? Label { get; set; }
    public int Repeat { get; set; } = 1;
    public List<WorkoutStep>? Steps { get; set; }

    public double PowerMid => (PowerLow + PowerHigh) / 2;

    public int TotalSeconds => Kind == StepKind.Repeat
        ? Repeat * (Steps?.Sum(s => s.TotalSeconds) ?? 0)
        : DurationSec;

    public static WorkoutStep Make(StepKind kind, int seconds, double low, double? high = null, string? label = null) =>
        new() { Kind = kind, DurationSec = seconds, PowerLow = low, PowerHigh = high ?? low, Label = label };

    public static WorkoutStep RepeatOf(int count, params WorkoutStep[] steps) =>
        new() { Kind = StepKind.Repeat, Repeat = count, Steps = [.. steps] };

    public WorkoutStep Clone() => new()
    {
        Kind = Kind, DurationSec = DurationSec, PowerLow = PowerLow, PowerHigh = PowerHigh, Label = Label,
        Repeat = Repeat, Steps = Steps?.Select(s => s.Clone()).ToList(),
    };

    /// <summary>Flattens repeats into a linear list of timed steps.</summary>
    public static IEnumerable<WorkoutStep> Flatten(IEnumerable<WorkoutStep> steps)
    {
        foreach (var s in steps)
        {
            if (s.Kind == StepKind.Repeat)
            {
                for (var i = 0; i < s.Repeat; i++)
                    foreach (var c in Flatten(s.Steps ?? []))
                        yield return c;
            }
            else
            {
                yield return s;
            }
        }
    }
}
