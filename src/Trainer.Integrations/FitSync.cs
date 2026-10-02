using Trainer.Core.Models;
using Trainer.Core.Training;
using Trainer.Data.Services;
using Trainer.Integrations.Edge;
using Trainer.Integrations.Fit;

namespace Trainer.Integrations;

public record SyncReport(int Added, int Replaced, int Duplicates, List<string> Errors)
{
    public static SyncReport From(IEnumerable<ImportResult> results, List<string> errors)
    {
        var list = results.ToList();
        return new SyncReport(list.Count(r => r.Outcome == ImportOutcome.Added),
            list.Count(r => r.Outcome == ImportOutcome.ReplacedStrava),
            list.Count(r => r.Outcome is ImportOutcome.Duplicate or ImportOutcome.Empty), errors);
    }

    /// <summary>Extra line for the rider, e.g. that a long Strava history sync stopped at the daily limit.</summary>
    public string? Note { get; init; }

    public override string ToString()
    {
        var parts = new List<string> { $"{Added} new ride{(Added == 1 ? "" : "s")}" };
        if (Replaced > 0) parts.Add($"{Replaced} Strava ride{(Replaced == 1 ? "" : "s")} upgraded to the FIT file");
        if (Duplicates > 0) parts.Add($"{Duplicates} already imported");
        if (Errors.Count > 0) parts.Add($"{Errors.Count} failed");
        return string.Join(", ", parts) + "." + (Note is null ? "" : " " + Note);
    }
}

/// <summary>Workouts out to the Edge as FIT files; rides in from the Edge or any folder of FIT files.</summary>
public class FitSync(TrainerService service)
{
    /// <summary>Writes workouts into the Edge's NewFiles folder. Watts use the FTP on each workout's date.</summary>
    public List<string> SendToEdge(IEnumerable<PlannedWorkout> workouts, IEdgeDevice device)
    {
        var history = service.GetFtpHistory();
        var written = new List<string>();
        foreach (var w in workouts.Where(w => w.Steps.Count > 0))
        {
            var name = FitWorkoutWriter.FileName(w);
            device.WriteNewFile(name, FitWorkoutWriter.Write(w, Ftp.On(history, w.Date)));
            written.Add(name);
        }
        return written;
    }

    /// <summary>Fallback when the Edge can't be reached: save the FIT files to a folder to copy by hand.</summary>
    public List<string> SaveToFolder(IEnumerable<PlannedWorkout> workouts, string folder)
    {
        Directory.CreateDirectory(folder);
        var history = service.GetFtpHistory();
        var written = new List<string>();
        foreach (var w in workouts.Where(w => w.Steps.Count > 0))
        {
            var path = Path.Combine(folder, FitWorkoutWriter.FileName(w));
            File.WriteAllBytes(path, FitWorkoutWriter.Write(w, Ftp.On(history, w.Date)));
            written.Add(path);
        }
        return written;
    }

    /// <summary>Copies new rides from the Edge's Activity folder into the app's FIT folder and imports them.</summary>
    public SyncReport ImportFromEdge(IEdgeDevice device)
    {
        var errors = new List<string>();
        var rides = new List<(RideData, ActivitySource)>();
        service.Paths.Ensure();
        foreach (var f in device.ListActivities())
        {
            var local = Path.Combine(service.Paths.FitFolder, f.Name);
            if (File.Exists(local) && new FileInfo(local).Length == f.Size) continue;
            try
            {
                using (var src = device.OpenActivity(f.Name))
                using (var dst = File.Create(local))
                    src.CopyTo(dst);
                rides.Add((FitActivityReader.Read(local), ActivitySource.Fit));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or Dynastream.Fit.FitException)
            {
                errors.Add($"{f.Name}: {ex.Message}");
            }
        }
        return SyncReport.From(service.ImportRides(rides), errors);
    }

    /// <summary>Imports FIT files from anywhere (file picker, watched folder). Files are copied into the app's folder.</summary>
    public SyncReport ImportFiles(IEnumerable<string> paths)
    {
        var errors = new List<string>();
        var rides = new List<(RideData, ActivitySource)>();
        service.Paths.Ensure();
        foreach (var path in paths)
        {
            try
            {
                var local = Path.Combine(service.Paths.FitFolder, Path.GetFileName(path));
                var samePath = string.Equals(Path.GetFullPath(path), Path.GetFullPath(local), StringComparison.OrdinalIgnoreCase);
                var alreadyCopied = File.Exists(local) && new FileInfo(local).Length == new FileInfo(path).Length;
                if (!samePath && !alreadyCopied) File.Copy(path, local, overwrite: true);
                rides.Add((FitActivityReader.Read(local), ActivitySource.Fit));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or Dynastream.Fit.FitException)
            {
                errors.Add($"{Path.GetFileName(path)}: {ex.Message}");
            }
        }
        return SyncReport.From(service.ImportRides(rides), errors);
    }
}
