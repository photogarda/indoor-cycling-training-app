namespace Trainer.Core.Training;

/// <summary>One recorded sample from a ride file or stream.</summary>
public readonly record struct RideSample(DateTime Time, double? Power, double? HeartRate, double? Cadence);

/// <summary>Everything an importer extracts from a ride before it is stored.</summary>
public class RideData
{
    public string Name { get; set; } = "Ride";
    public DateTime StartTime { get; set; }
    public int? TimerSeconds { get; set; }
    public double? DistanceKm { get; set; }
    public List<RideSample> Samples { get; set; } = [];
    public string? FilePath { get; set; }
    public long? StravaId { get; set; }
}

public record RideSummary(
    int DurationSec,
    double? AvgPower,
    double? NormalizedPower,
    double? IntensityFactor,
    double Tss,
    bool HrBasedTss,
    double? AvgHr,
    double? AvgCadence,
    Dictionary<int, double> PowerCurve);

public static class RideAnalyzer
{
    /// <summary>
    /// Resamples to 1 Hz (short dropouts up to 5 s hold the last value; longer gaps such as auto-pause are
    /// skipped) and works out the ride summary.
    /// </summary>
    public static RideSummary Summarize(RideData ride, int ftp, int? thresholdHr)
    {
        var samples = ride.Samples.OrderBy(s => s.Time).ToList();
        var power = new List<double>();
        var hr = new List<double>();
        var cadence = new List<double>();
        var hasPower = samples.Any(s => s.Power > 0);

        for (var i = 0; i < samples.Count; i++)
        {
            var s = samples[i];
            var gap = i + 1 < samples.Count ? (int)Math.Round((samples[i + 1].Time - s.Time).TotalSeconds) : 1;
            var repeat = gap is >= 1 and <= 5 ? gap : 1;
            for (var r = 0; r < repeat; r++)
            {
                power.Add(s.Power ?? 0);
                if (s.HeartRate is > 0) hr.Add(s.HeartRate.Value);
                if (s.Cadence is > 0) cadence.Add(s.Cadence.Value);
            }
        }

        var duration = ride.TimerSeconds ?? power.Count;
        double? avgHr = hr.Count > 0 ? hr.Average() : null;
        double? avgCad = cadence.Count > 0 ? cadence.Average() : null;

        if (hasPower)
        {
            var np = LoadMath.NormalizedPower(power) ?? power.Average();
            var ifactor = ftp > 0 ? np / ftp : (double?)null;
            var tss = LoadMath.Tss(power.Count, np, ftp);
            return new RideSummary(duration, Math.Round(power.Average(), 1), Math.Round(np, 1),
                ifactor is null ? null : Math.Round(ifactor.Value, 3), Math.Round(tss, 1), false,
                avgHr is null ? null : Math.Round(avgHr.Value), avgCad is null ? null : Math.Round(avgCad.Value),
                PowerCurve.Compute(power));
        }

        var hrTss = avgHr is not null && thresholdHr is > 0 ? LoadMath.HrTss(duration, avgHr.Value, thresholdHr.Value) : 0;
        return new RideSummary(duration, null, null, null, Math.Round(hrTss, 1), true,
            avgHr is null ? null : Math.Round(avgHr.Value), avgCad is null ? null : Math.Round(avgCad.Value), []);
    }
}
