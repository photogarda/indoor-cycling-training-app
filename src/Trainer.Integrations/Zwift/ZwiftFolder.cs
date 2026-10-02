using Trainer.Core.Models;

namespace Trainer.Integrations.Zwift;

/// <summary>Where Zwift looks for custom workouts: Documents\Zwift\Workouts\&lt;your Zwift id&gt;.</summary>
public static class ZwiftFolder
{
    /// <summary>The per-account workouts folder if Zwift is installed, else null.</summary>
    public static string? Find()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Zwift", "Workouts");
        if (!Directory.Exists(root)) return null;
        // Zwift creates one numeric folder per account; pick the most recently used.
        return Directory.EnumerateDirectories(root)
            .Where(d => Path.GetFileName(d).All(char.IsDigit))
            .OrderByDescending(Directory.GetLastWriteTimeUtc)
            .FirstOrDefault() ?? root;
    }

    /// <summary>Writes each workout as a .zwo file into a folder. Returns the paths written.</summary>
    public static List<string> SaveAll(IEnumerable<PlannedWorkout> workouts, string folder)
    {
        Directory.CreateDirectory(folder);
        var written = new List<string>();
        foreach (var w in workouts.Where(w => w.Steps.Count > 0))
        {
            var path = Path.Combine(folder, ZwoWriter.FileName(w.Name, w.Date));
            File.WriteAllText(path, ZwoWriter.Write(w));
            written.Add(path);
        }
        return written;
    }
}
