using Dynastream.Fit;
using Trainer.Core.Training;

namespace Trainer.Integrations.Fit;

/// <summary>Reads a ride from a FIT activity file: power, heart rate and cadence records plus the session summary.</summary>
public static class FitActivityReader
{
    public static RideData Read(string path)
    {
        using var fs = System.IO.File.OpenRead(path);
        var ride = Read(fs, Path.GetFileNameWithoutExtension(path));
        ride.FilePath = path;
        return ride;
    }

    public static RideData Read(Stream stream, string fallbackName = "Ride")
    {
        var decode = new Decode();
        if (!decode.IsFIT(stream)) throw new InvalidDataException("Not a FIT file.");
        stream.Position = 0;

        var ride = new RideData { Name = fallbackName };
        Dynastream.Fit.File? fileType = null;
        System.DateTime? sessionStart = null;
        Sport? sport = null;
        SubSport? subSport = null;
        float timer = 0;
        float distance = 0;

        var broadcaster = new MesgBroadcaster();
        decode.MesgEvent += broadcaster.OnMesg;
        broadcaster.FileIdMesgEvent += (_, e) => fileType = new FileIdMesg(e.mesg).GetType();
        broadcaster.RecordMesgEvent += (_, e) =>
        {
            var r = new RecordMesg(e.mesg);
            var ts = r.GetTimestamp();
            if (ts is null) return;
            ride.Samples.Add(new RideSample(
                System.DateTime.SpecifyKind(ts.GetDateTime(), DateTimeKind.Utc).ToLocalTime(),
                r.GetPower(), r.GetHeartRate(), r.GetCadence()));
        };
        broadcaster.SessionMesgEvent += (_, e) =>
        {
            var s = new SessionMesg(e.mesg);
            var st = s.GetStartTime();
            if (st is not null && sessionStart is null)
                sessionStart = System.DateTime.SpecifyKind(st.GetDateTime(), DateTimeKind.Utc).ToLocalTime();
            timer += s.GetTotalTimerTime() ?? 0;
            distance += s.GetTotalDistance() ?? 0;
            sport ??= s.GetSport();
            subSport ??= s.GetSubSport();
        };

        // Lenient read: Edge files cut short (battery, crash) still carry useful records.
        try
        {
            decode.Read(stream, DecodeMode.Normal);
        }
        catch (FitException) when (ride.Samples.Count > 0)
        {
        }

        if (fileType is not null && fileType != Dynastream.Fit.File.Activity)
            throw new InvalidDataException($"FIT file is a {fileType}, not an activity.");

        ride.StartTime = sessionStart ?? (ride.Samples.Count > 0 ? ride.Samples[0].Time : System.DateTime.Now);
        ride.TimerSeconds = timer > 0 ? (int)Math.Round(timer) : null;
        ride.DistanceKm = distance > 0 ? distance / 1000 : null;
        var where = subSport == SubSport.IndoorCycling || sport == Sport.Training ? "Indoor ride" : "Ride";
        if (fallbackName == "Ride" || char.IsDigit(fallbackName.FirstOrDefault())) ride.Name = $"{where} {ride.StartTime:d MMM}";
        return ride;
    }
}
