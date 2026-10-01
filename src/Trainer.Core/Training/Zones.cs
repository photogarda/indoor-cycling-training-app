namespace Trainer.Core.Training;

public enum PowerZone { Z1Recovery, Z2Endurance, Z3Tempo, SweetSpot, Z4Threshold, Z5Vo2Max, Z6Anaerobic, Z7Sprint }

/// <summary>Power zones as % FTP (Coggan-style, with sweet spot as its own band).</summary>
public static class Zones
{
    public static readonly IReadOnlyList<(PowerZone Zone, string Name, double Low, double High)> Table =
    [
        (PowerZone.Z1Recovery, "Z1 Recovery", 0.00, 0.55),
        (PowerZone.Z2Endurance, "Z2 Endurance", 0.56, 0.75),
        (PowerZone.Z3Tempo, "Z3 Tempo", 0.76, 0.90),
        (PowerZone.SweetSpot, "Sweet spot", 0.88, 0.94),
        (PowerZone.Z4Threshold, "Z4 Threshold", 0.91, 1.05),
        (PowerZone.Z5Vo2Max, "Z5 VO2 max", 1.06, 1.20),
        (PowerZone.Z6Anaerobic, "Z6 Anaerobic", 1.21, 1.50),
        (PowerZone.Z7Sprint, "Z7 Sprint", 1.51, 9.99),
    ];

    /// <summary>
    /// Zone for a fraction of FTP. Overlapping bands are resolved so that 88–94 % reads as sweet spot,
    /// above that threshold.
    /// </summary>
    public static PowerZone Of(double fractionOfFtp) => fractionOfFtp switch
    {
        <= 0.555 => PowerZone.Z1Recovery,
        <= 0.755 => PowerZone.Z2Endurance,
        < 0.875 => PowerZone.Z3Tempo,
        <= 0.945 => PowerZone.SweetSpot,
        <= 1.055 => PowerZone.Z4Threshold,
        <= 1.205 => PowerZone.Z5Vo2Max,
        <= 1.505 => PowerZone.Z6Anaerobic,
        _ => PowerZone.Z7Sprint,
    };

    public static string Name(PowerZone z) => Table.First(t => t.Zone == z).Name;

    public static (int Low, int High) Watts(PowerZone z, int ftp)
    {
        var row = Table.First(t => t.Zone == z);
        return ((int)Math.Round(row.Low * ftp), z == PowerZone.Z7Sprint ? 9999 : (int)Math.Round(row.High * ftp));
    }
}
