namespace Trainer.Core.Training;

/// <summary>NP, IF, TSS and hrTSS. Power samples are 1 Hz watts.</summary>
public static class LoadMath
{
    /// <summary>
    /// Normalized power: 4th root of the mean of the 30-second rolling average power raised to the 4th power.
    /// Returns null for rides shorter than 30 s.
    /// </summary>
    public static double? NormalizedPower(IReadOnlyList<double> watts)
    {
        const int window = 30;
        if (watts.Count < window) return null;
        double rolling = 0, sum4 = 0;
        for (var i = 0; i < window; i++) rolling += watts[i];
        var n = 0;
        for (var i = window - 1; i < watts.Count; i++)
        {
            if (i >= window) rolling += watts[i] - watts[i - window];
            var avg = rolling / window;
            sum4 += avg * avg * avg * avg;
            n++;
        }
        return Math.Pow(sum4 / n, 0.25);
    }

    public static double IntensityFactor(double np, double ftp) => ftp <= 0 ? 0 : np / ftp;

    /// <summary>TSS = (t · NP · IF) / (FTP · 3600) · 100.</summary>
    public static double Tss(double seconds, double np, double ftp)
    {
        if (ftp <= 0) return 0;
        var intensity = np / ftp;
        return seconds * np * intensity / (ftp * 3600) * 100;
    }

    /// <summary>
    /// Heart-rate TSS for rides without power: the ride's average HR as a fraction of threshold HR is used
    /// as the intensity factor. A simple, widely used approximation.
    /// </summary>
    public static double HrTss(double seconds, double avgHr, double thresholdHr)
    {
        if (thresholdHr <= 0 || avgHr <= 0) return 0;
        var intensity = avgHr / thresholdHr;
        return seconds / 3600 * intensity * intensity * 100;
    }

    /// <summary>TSS of a prescribed workout from its steps (fractions of FTP), so FTP drops out.</summary>
    public static (double Tss, double If, int Seconds) ForSteps(IEnumerable<Models.WorkoutStep> steps)
    {
        var flat = Models.WorkoutStep.Flatten(steps).ToList();
        var seconds = flat.Sum(s => s.DurationSec);
        if (seconds == 0) return (0, 0, 0);
        // Expand to 1 Hz so NP smoothing behaves like a ride. Free (ERG off) steps assume ~1.3x FTP surges.
        var samples = new List<double>(seconds);
        foreach (var s in flat)
        {
            var p = s.Kind == Models.StepKind.Free ? Math.Max(s.PowerMid, 1.3) : s.PowerMid;
            for (var i = 0; i < s.DurationSec; i++) samples.Add(p * 100);
        }
        var np = NormalizedPower(samples) ?? samples.Average();
        var tss = Tss(seconds, np, 100);
        return (tss, np / 100, seconds);
    }
}
