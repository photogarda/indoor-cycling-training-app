using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Trainer.Core.Models;

namespace Trainer.Integrations.Zwift;

/// <summary>
/// Writes a workout as a Zwift <c>.zwo</c> file (also read by TrainerRoad, MyWhoosh, TrainingPeaks Virtual
/// and zwofactory.com). Power stays as a fraction of FTP, so the riding app applies its own FTP.
/// </summary>
/// <remarks>
/// Mapping: warm-up and cool-down become <c>Warmup</c>/<c>Cooldown</c> ramps, ERG-off steps become
/// <c>FreeRide</c>, and a repeat of exactly one "on" and one "off" step becomes <c>IntervalsT</c>.
/// Anything else (nested repeats, three-step sets) is written out step by step. Step labels become
/// on-screen text messages.
/// </remarks>
public static class ZwoWriter
{
    public static string Write(string name, IReadOnlyList<WorkoutStep> steps, string? description = null,
        string author = "Cycling Training Planner", WorkoutKind? kind = null)
    {
        var workout = new XElement("workout");
        foreach (var s in steps) Add(workout, s);
        if (!workout.HasElements) throw new InvalidOperationException("The workout has no steps.");

        var file = new XElement("workout_file",
            new XElement("author", author),
            new XElement("name", name),
            new XElement("description", description ?? ""),
            new XElement("sportType", "bike"));
        if (kind is { } k)
            file.Add(new XElement("tags", new XElement("tag", new XAttribute("name", k.Display().ToUpperInvariant()))));
        file.Add(workout);

        var sb = new StringBuilder();
        using (var w = new Utf8StringWriter(sb)) new XDocument(file).Save(w);
        return sb.ToString();
    }

    public static string Write(PlannedWorkout w) => Write(w.Name, w.Steps, w.Notes, kind: w.Kind);

    public static string Write(WorkoutTemplate t) => Write(t.Name, t.Steps, t.Description, kind: t.Kind);

    /// <summary>A file name Zwift accepts: date (if any) plus name, letters and digits only.</summary>
    public static string FileName(string name, DateOnly? date = null)
    {
        var clean = new string(name.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray()).Trim('_');
        return (date is { } d ? $"{d:yyyyMMdd}_" : "") + clean + ".zwo";
    }

    private static void Add(XElement parent, WorkoutStep s)
    {
        if (s.Kind == StepKind.Repeat)
        {
            var children = s.Steps ?? [];
            if (children.Count == 2 && children.All(c => c.Kind is not (StepKind.Repeat or StepKind.Free)) && s.Repeat > 1)
            {
                var on = children[0];
                var off = children[1];
                var el = new XElement("IntervalsT",
                    new XAttribute("Repeat", s.Repeat),
                    new XAttribute("OnDuration", on.DurationSec),
                    new XAttribute("OffDuration", off.DurationSec),
                    new XAttribute("OnPower", F(on.PowerMid)),
                    new XAttribute("OffPower", F(off.PowerMid)));
                Text(el, on.Label);
                parent.Add(el);
                return;
            }
            for (var i = 0; i < s.Repeat; i++)
                foreach (var c in children) Add(parent, c);
            return;
        }

        if (s.DurationSec <= 0) return;
        XElement step = s.Kind switch
        {
            StepKind.Free => new XElement("FreeRide", new XAttribute("Duration", s.DurationSec), new XAttribute("FlatRoad", 1)),
            StepKind.Warmup => Ramp("Warmup", s.DurationSec, s.PowerLow, s.PowerHigh),
            // Zwift ramps a cool-down from PowerLow to PowerHigh, so start at the top of the range.
            StepKind.Cooldown => Ramp("Cooldown", s.DurationSec, s.PowerHigh, s.PowerLow),
            _ => new XElement("SteadyState", new XAttribute("Duration", s.DurationSec), new XAttribute("Power", F(s.PowerMid))),
        };
        Text(step, s.Label);
        parent.Add(step);
    }

    private static XElement Ramp(string name, int seconds, double from, double to) =>
        new(name, new XAttribute("Duration", seconds), new XAttribute("PowerLow", F(from)), new XAttribute("PowerHigh", F(to)));

    private static void Text(XElement step, string? label)
    {
        if (!string.IsNullOrWhiteSpace(label))
            step.Add(new XElement("textevent", new XAttribute("timeoffset", 0), new XAttribute("message", label)));
    }

    private static string F(double v) => Math.Round(v, 3).ToString(CultureInfo.InvariantCulture);

    private sealed class Utf8StringWriter(StringBuilder sb) : StringWriter(sb, CultureInfo.InvariantCulture)
    {
        public override Encoding Encoding => new UTF8Encoding(false);
    }
}
