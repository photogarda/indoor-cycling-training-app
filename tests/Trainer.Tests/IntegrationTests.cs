using Dynastream.Fit;
using Trainer.Core.Models;
using Trainer.Core.Workouts;
using Trainer.Data.Services;
using Trainer.Integrations;
using Trainer.Integrations.Edge;
using Trainer.Integrations.Fit;
using Trainer.Integrations.Strava;
using DateTime = System.DateTime;
using File = System.IO.File;

namespace Trainer.Tests;

public class IntegrationTests
{
    private static PlannedWorkout Workout(string notation, string name = "Threshold 2×20") => new()
    {
        Name = name, Date = new DateOnly(2026, 10, 6), Steps = IntervalNotation.Parse(notation), Indoor = true,
    };

    private static List<WorkoutStepMesg> DecodeSteps(byte[] bytes, out WorkoutMesg? workout, out FileIdMesg? fileId)
    {
        var steps = new List<WorkoutStepMesg>();
        WorkoutMesg? w = null;
        FileIdMesg? f = null;
        var decode = new Decode();
        var b = new MesgBroadcaster();
        decode.MesgEvent += b.OnMesg;
        b.WorkoutStepMesgEvent += (_, e) => steps.Add(new WorkoutStepMesg(e.mesg));
        b.WorkoutMesgEvent += (_, e) => w = new WorkoutMesg(e.mesg);
        b.FileIdMesgEvent += (_, e) => f = new FileIdMesg(e.mesg);
        using var ms = new MemoryStream(bytes);
        Assert.True(decode.IsFIT(ms));
        ms.Position = 0;
        Assert.True(decode.CheckIntegrity(ms));
        ms.Position = 0;
        decode.Read(ms);
        workout = w;
        fileId = f;
        return steps;
    }

    [Fact]
    public void Workout_fit_has_watts_from_ftp_and_repeat_steps()
    {
        var bytes = FitWorkoutWriter.Write(Workout("wu 10m 50-70; 2x(20m 95-100 \"threshold\", 5m 55); free 30s \"sprint\"; cd 5m 50"), 250);
        var steps = DecodeSteps(bytes, out var wkt, out var fileId);

        Assert.Equal(Dynastream.Fit.File.Workout, fileId!.GetType());
        Assert.Equal("Threshold 2×20", wkt!.GetWktNameAsString());
        Assert.Equal(Sport.Cycling, wkt.GetSport());
        Assert.Equal((ushort)steps.Count, wkt.GetNumValidSteps());
        Assert.Equal(6, steps.Count); // wu, work, rest, repeat, free, cd

        Assert.Equal(WktStepTarget.Power, steps[0].GetTargetType());
        Assert.Equal(125u + 1000, steps[0].GetCustomTargetPowerLow());
        Assert.Equal(175u + 1000, steps[0].GetCustomTargetPowerHigh());
        Assert.Equal(600f, steps[0].GetDurationTime());
        Assert.Equal(Intensity.Warmup, steps[0].GetIntensity());

        Assert.Equal(238u + 1000, steps[1].GetCustomTargetPowerLow());
        Assert.Equal(250u + 1000, steps[1].GetCustomTargetPowerHigh());
        Assert.Equal("threshold", steps[1].GetWktStepNameAsString());

        Assert.Equal(WktStepDuration.RepeatUntilStepsCmplt, steps[3].GetDurationType());
        Assert.Equal(1u, steps[3].GetDurationStep());
        Assert.Equal(2u, steps[3].GetRepeatSteps());

        Assert.Equal(WktStepTarget.Open, steps[4].GetTargetType());
    }

    [Fact]
    public void New_ftp_changes_exported_watts()
    {
        var w = Workout("20m 100");
        var low = DecodeSteps(FitWorkoutWriter.Write(w, 200), out _, out _)[0].GetCustomTargetPowerLow();
        var high = DecodeSteps(FitWorkoutWriter.Write(w, 300), out _, out _)[0].GetCustomTargetPowerLow();
        Assert.Equal(196u + 1000, low);
        Assert.Equal(294u + 1000, high);
    }

    [Fact]
    public void Whole_library_exports_as_valid_fit()
    {
        foreach (var t in WorkoutLibrary.BuiltIn())
        {
            var bytes = FitWorkoutWriter.Write(new PlannedWorkout { Name = t.Name, Steps = t.Steps }, 250);
            Assert.NotEmpty(DecodeSteps(bytes, out _, out _));
        }
    }

    /// <summary>Writes a small activity FIT like an Edge would.</summary>
    public static byte[] ActivityFit(DateTime startUtc, int seconds, Func<int, ushort> power, byte hr = 140, byte cadence = 90)
    {
        using var ms = new MemoryStream();
        var enc = new Encode(ProtocolVersion.V20);
        enc.Open(ms);
        var id = new FileIdMesg();
        id.SetType(Dynastream.Fit.File.Activity);
        id.SetManufacturer(Manufacturer.Garmin);
        id.SetProduct(3843);
        id.SetSerialNumber(12345);
        id.SetTimeCreated(new Dynastream.Fit.DateTime(startUtc));
        enc.Write(id);
        for (var i = 0; i < seconds; i++)
        {
            var r = new RecordMesg();
            r.SetTimestamp(new Dynastream.Fit.DateTime(startUtc.AddSeconds(i)));
            r.SetPower(power(i));
            r.SetHeartRate(hr);
            r.SetCadence(cadence);
            enc.Write(r);
        }
        var s = new SessionMesg();
        s.SetTimestamp(new Dynastream.Fit.DateTime(startUtc.AddSeconds(seconds)));
        s.SetStartTime(new Dynastream.Fit.DateTime(startUtc));
        s.SetSport(Sport.Cycling);
        s.SetSubSport(SubSport.IndoorCycling);
        s.SetTotalTimerTime(seconds);
        s.SetTotalElapsedTime(seconds);
        s.SetTotalDistance(seconds * 9.0f);
        enc.Write(s);
        enc.Close();
        return ms.ToArray();
    }

    [Fact]
    public void Activity_fit_is_read_into_samples()
    {
        var start = new DateTime(2026, 9, 29, 17, 0, 0, DateTimeKind.Utc);
        var bytes = ActivityFit(start, 1200, i => (ushort)(i < 600 ? 200 : 300));
        var ride = FitActivityReader.Read(new MemoryStream(bytes));
        Assert.Equal(1200, ride.Samples.Count);
        Assert.Equal(start.ToLocalTime(), ride.StartTime);
        Assert.Equal(1200, ride.TimerSeconds);
        Assert.Equal(10.8, ride.DistanceKm!.Value, 2);
        Assert.Equal(250, ride.Samples.Average(s => s.Power!.Value), 3);
        Assert.StartsWith("Indoor ride", ride.Name);
    }

    [Fact]
    public void Workout_file_is_rejected_as_a_ride()
    {
        var bytes = FitWorkoutWriter.Write(Workout("20m 100"), 250);
        Assert.Throws<InvalidDataException>(() => FitActivityReader.Read(new MemoryStream(bytes)));
    }

    [Fact]
    public void Edge_drive_round_trip_send_week_and_import_rides()
    {
        using var t = new TestDb();
        t.Service.AddFtp(250, FtpMethod.Manual);
        var edgeRoot = Path.Combine(t.Paths.Root, "edge");
        Directory.CreateDirectory(Path.Combine(edgeRoot, "Garmin", "Activity"));
        var planned = t.Service.GetWorkouts(t.Today, t.Today.AddDays(6));

        using var edge = Assert.Single(EdgeLocator.FindAll(edgeRoot));
        var sync = new FitSync(t.Service);
        var sent = sync.SendToEdge(planned, edge);
        Assert.Equal(planned.Count, sent.Count);
        Assert.All(sent, f => Assert.True(File.Exists(Path.Combine(edgeRoot, "Garmin", "NewFiles", f))));

        var first = planned[0];
        var rideStart = first.Date.ToDateTime(new TimeOnly(18, 0)).ToUniversalTime();
        File.WriteAllBytes(Path.Combine(edgeRoot, "Garmin", "Activity", "2026-10-01-18-00-00.fit"),
            ActivityFit(rideStart, first.DurationSec, _ => (ushort)(250 * first.IntensityFactor)));
        t.Today = first.Date.AddDays(1);

        var report = sync.ImportFromEdge(edge);
        Assert.Equal(1, report.Added);
        Assert.Empty(report.Errors);
        Assert.True(File.Exists(Path.Combine(t.Paths.FitFolder, "2026-10-01-18-00-00.fit")));
        Assert.Equal(first.Id, t.Service.GetActivities().Single().PlannedWorkoutId);

        // Plugging in again imports nothing new.
        Assert.Equal(0, sync.ImportFromEdge(edge).Added);
    }

    [Fact]
    public async Task Watched_folder_imports_new_files()
    {
        using var t = new TestDb();
        t.Service.AddFtp(250, FtpMethod.Manual);
        var folder = Path.Combine(t.Paths.Root, "downloads");
        Directory.CreateDirectory(folder);
        using var watcher = new FolderWatcher(new FitSync(t.Service), folder);
        var done = new TaskCompletionSource<SyncReport>();
        watcher.Imported += (_, r) => done.TrySetResult(r);
        watcher.Start();
        File.WriteAllBytes(Path.Combine(folder, "123_ACTIVITY.fit"), ActivityFit(new DateTime(2026, 9, 28, 6, 0, 0, DateTimeKind.Utc), 1800, _ => 210));
        var report = await done.Task.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(1, report.Added);
    }

    [Fact]
    public void Strava_streams_become_a_ride()
    {
        const string json = """
        {"time":{"data":[0,1,2,4]},"watts":{"data":[200,210,null,230]},"heartrate":{"data":[120,121,122,123]},"cadence":{"data":[88,89,90,91]}}
        """;
        var summary = new StravaActivitySummary(99, "Zwift", "VirtualRide", new DateTime(2026, 9, 1, 6, 0, 0, DateTimeKind.Utc), 4, 1000);
        var ride = StravaClient.FromStreams(summary, json);
        Assert.Equal(4, ride.Samples.Count);
        Assert.Equal(99, ride.StravaId);
        Assert.Null(ride.Samples[2].Power);
        Assert.Equal(ride.StartTime.AddSeconds(4), ride.Samples[3].Time);
        Assert.Equal(1.0, ride.DistanceKm);
    }

    [Fact]
    public void Strava_authorize_url_requests_activity_read()
    {
        var url = StravaClient.AuthorizeUrl("12345");
        Assert.Contains("client_id=12345", url);
        Assert.Contains("activity%3Aread_all", url.Replace(":", "%3A"));
        Assert.Contains(Uri.EscapeDataString(StravaClient.RedirectUri), url);
    }
}
