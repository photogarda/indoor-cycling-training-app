namespace Trainer.Core.Training;

/// <summary>Mean-maximal power for standard durations, 5 s to 60 min.</summary>
public static class PowerCurve
{
    public static readonly int[] Durations = [5, 10, 15, 30, 60, 120, 180, 300, 480, 600, 720, 900, 1200, 1800, 2400, 3600];

    public static Dictionary<int, double> Compute(IReadOnlyList<double> watts)
    {
        var result = new Dictionary<int, double>();
        if (watts.Count == 0) return result;
        var prefix = new double[watts.Count + 1];
        for (var i = 0; i < watts.Count; i++) prefix[i + 1] = prefix[i] + watts[i];
        foreach (var d in Durations)
        {
            if (d > watts.Count) break;
            var best = 0.0;
            for (var i = d; i <= watts.Count; i++)
                best = Math.Max(best, prefix[i] - prefix[i - d]);
            result[d] = Math.Round(best / d, 1);
        }
        return result;
    }

    /// <summary>Best value per duration across many rides.</summary>
    public static Dictionary<int, double> Merge(IEnumerable<IReadOnlyDictionary<int, double>> curves)
    {
        var result = new Dictionary<int, double>();
        foreach (var c in curves)
            foreach (var (d, w) in c)
                if (!result.TryGetValue(d, out var cur) || w > cur) result[d] = w;
        return result;
    }

    public static string Label(int seconds) => seconds < 60 ? $"{seconds} s" : $"{seconds / 60} min";
}
