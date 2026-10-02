using System.Globalization;
using System.Xml.Linq;
using Trainer.Core.Models;
using Trainer.Core.Workouts;
using Trainer.Integrations.Zwift;

namespace Trainer.Tests;

public class ZwoTests
{
    private static XElement Workout(string notation, string name = "Test") =>
        XDocument.Parse(ZwoWriter.Write(name, IntervalNotation.Parse(notation))).Root!.Element("workout")!;

    private static double D(XElement e, string attr) => double.Parse(e.Attribute(attr)!.Value, CultureInfo.InvariantCulture);

    [Fact]
    public void Writes_zwift_header_and_steps()
    {
        var doc = XDocument.Parse(ZwoWriter.Write("Threshold 2×20", IntervalNotation.Parse("wu 10m 50-70; 20m 95-100; cd 8m 50"), "Steady FTP work", kind: WorkoutKind.Threshold));
        var root = doc.Root!;
        Assert.Equal("workout_file", root.Name.LocalName);
        Assert.Equal("Threshold 2×20", root.Element("name")!.Value);
        Assert.Equal("bike", root.Element("sportType")!.Value);
        Assert.Equal("Steady FTP work", root.Element("description")!.Value);
        Assert.Equal("THRESHOLD", root.Element("tags")!.Element("tag")!.Attribute("name")!.Value);

        var steps = root.Element("workout")!.Elements().ToList();
        Assert.Equal(["Warmup", "SteadyState", "Cooldown"], steps.Select(e => e.Name.LocalName));
        Assert.Equal(600, D(steps[0], "Duration"));
        Assert.Equal(0.5, D(steps[0], "PowerLow"));
        Assert.Equal(0.7, D(steps[0], "PowerHigh"));
        Assert.Equal(0.975, D(steps[1], "Power"));
    }

    [Fact]
    public void Two_step_repeat_becomes_IntervalsT_with_label()
    {
        var el = Workout("3x(10m 95-100 \"threshold\", 5m 55)").Elements().Single();
        Assert.Equal("IntervalsT", el.Name.LocalName);
        Assert.Equal(3, D(el, "Repeat"));
        Assert.Equal(600, D(el, "OnDuration"));
        Assert.Equal(300, D(el, "OffDuration"));
        Assert.Equal(0.975, D(el, "OnPower"));
        Assert.Equal(0.55, D(el, "OffPower"));
        Assert.Equal("threshold", el.Element("textevent")!.Attribute("message")!.Value);
    }

    [Fact]
    public void Nested_repeats_are_unrolled_and_free_steps_are_free_ride()
    {
        var steps = Workout("2x(3x(2m 93, 1m 106), 5m 55); free 30s \"sprint\"").Elements().ToList();
        // Each outer set: one IntervalsT (3 over-unders) + recovery.
        Assert.Equal(["IntervalsT", "SteadyState", "IntervalsT", "SteadyState", "FreeRide"], steps.Select(e => e.Name.LocalName));
        Assert.Equal(30, D(steps[^1], "Duration"));
    }

    [Fact]
    public void Cooldown_ramps_down()
    {
        var cd = Workout("cd 10m 40-65").Elements().Single();
        Assert.Equal(0.65, D(cd, "PowerLow"));
        Assert.Equal(0.4, D(cd, "PowerHigh"));
    }

    [Fact]
    public void Whole_library_exports_with_the_same_duration()
    {
        foreach (var t in WorkoutLibrary.BuiltIn())
        {
            var w = XDocument.Parse(ZwoWriter.Write(t)).Root!.Element("workout")!;
            var seconds = w.Elements().Sum(e => e.Name.LocalName == "IntervalsT"
                ? D(e, "Repeat") * (D(e, "OnDuration") + D(e, "OffDuration"))
                : D(e, "Duration"));
            Assert.Equal(t.Steps.Sum(s => s.TotalSeconds), seconds);
        }
    }

    [Fact]
    public void Saves_week_as_files()
    {
        var folder = Path.Combine(Path.GetTempPath(), "zwo-" + Guid.NewGuid().ToString("N"));
        try
        {
            var lib = WorkoutLibrary.BuiltIn();
            var files = ZwiftFolder.SaveAll(
            [
                new PlannedWorkout { Name = lib[10].Name, Date = new DateOnly(2026, 10, 6), Steps = lib[10].Steps },
                new PlannedWorkout { Name = "Empty", Date = new DateOnly(2026, 10, 7) },
            ], folder);
            var file = Assert.Single(files);
            Assert.StartsWith("20261006_", Path.GetFileName(file));
            Assert.EndsWith(".zwo", file);
            Assert.NotNull(XDocument.Load(file).Root!.Element("workout"));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }
}
