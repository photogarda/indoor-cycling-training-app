using Trainer.Core.Models;

namespace Trainer.Core.Training;

public static class Ftp
{
    /// <summary>The latest FTP entry on or before a date; the earliest entry if none precede it.</summary>
    public static int On(IEnumerable<FtpEntry> history, DateOnly date)
    {
        var list = history.OrderBy(h => h.Date).ToList();
        if (list.Count == 0) return 0;
        var before = list.LastOrDefault(h => h.Date <= date);
        return (before ?? list[0]).Watts;
    }

    /// <summary>Estimate: 95 % of the best 20-minute power in rides from the 6 weeks before <paramref name="today"/>.</summary>
    public static int? Estimate(IEnumerable<Activity> activities, DateOnly today)
    {
        var from = today.AddDays(-42);
        var best = activities
            .Where(a => a.Date > from && a.Date <= today)
            .Select(a => a.PowerCurve.TryGetValue(1200, out var w) ? w : 0)
            .DefaultIfEmpty(0)
            .Max();
        return best > 0 ? (int)Math.Round(best * 0.95) : null;
    }

    /// <summary>
    /// Ramp test result: 75 % of the best 1-minute power (the last completed minute of a 1-minute-step ramp).
    /// </summary>
    public static int FromRampTest(double bestOneMinutePower) => (int)Math.Round(bestOneMinutePower * 0.75);
}
