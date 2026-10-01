using Trainer.Core.Models;

namespace Trainer.Core.Training;

/// <summary>Performance management chart: CTL (42-day), ATL (7-day) and TSB.</summary>
public static class Pmc
{
    public const double CtlDays = 42;
    public const double AtlDays = 7;

    /// <summary>
    /// CTL_d = CTL_{d-1} + (TSS_d − CTL_{d-1}) / 42, ATL likewise over 7 days, TSB_d = CTL_{d-1} − ATL_{d-1}
    /// (form going into the day). Days with no rides count as zero TSS.
    /// </summary>
    public static List<DailyLoad> Compute(IReadOnlyDictionary<DateOnly, double> tssByDay, DateOnly from, DateOnly to,
        double seedCtl = 0, double seedAtl = 0)
    {
        var result = new List<DailyLoad>();
        double ctl = seedCtl, atl = seedAtl;
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            var tss = tssByDay.TryGetValue(d, out var t) ? t : 0;
            var tsb = ctl - atl;
            ctl += (tss - ctl) / CtlDays;
            atl += (tss - atl) / AtlDays;
            result.Add(new DailyLoad { Date = d, Tss = tss, Ctl = ctl, Atl = atl, Tsb = tsb });
        }
        return result;
    }

    /// <summary>Rolls CTL forward through daily TSS values, returning the final CTL.</summary>
    public static double Project(double ctl, IEnumerable<double> dailyTss)
    {
        foreach (var tss in dailyTss) ctl += (tss - ctl) / CtlDays;
        return ctl;
    }

    /// <summary>
    /// Largest average daily TSS for the coming week that keeps the CTL rise within <paramref name="rampPerWeek"/>.
    /// With constant daily TSS T, CTL after 7 days rises by (T − CTL₀)(1 − (41/42)^7).
    /// </summary>
    public static double MaxDailyTssForRamp(double ctl, double rampPerWeek)
    {
        var k = 1 - Math.Pow(1 - 1 / CtlDays, 7);
        return ctl + rampPerWeek / k;
    }
}
