namespace Trainer.Integrations.Edge;

/// <summary>An Edge mounted as a drive letter (or any folder that contains a <c>Garmin</c> folder).</summary>
public sealed class DriveEdgeDevice : IEdgeDevice
{
    private readonly string _garmin;

    public DriveEdgeDevice(string root)
    {
        Root = root;
        _garmin = Path.GetFileName(root.TrimEnd('\\', '/')).Equals("Garmin", StringComparison.OrdinalIgnoreCase)
            ? root
            : Path.Combine(root, "Garmin");
        if (!Directory.Exists(_garmin)) throw new DirectoryNotFoundException($"No Garmin folder under {root}.");
    }

    public string Root { get; }
    public string DisplayName => $"Garmin on {Root}";

    public static bool LooksLikeGarmin(string root) =>
        Directory.Exists(Path.Combine(root, "Garmin")) || Path.GetFileName(root.TrimEnd('\\', '/')).Equals("Garmin", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<EdgeFile> ListActivities()
    {
        var dir = Path.Combine(_garmin, "Activity");
        if (!Directory.Exists(dir)) return [];
        return Directory.EnumerateFiles(dir, "*.fit", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(dir, "*.FIT", SearchOption.TopDirectoryOnly))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(f => new FileInfo(f))
            .Select(f => new EdgeFile(f.Name, f.Length, f.LastWriteTime))
            .OrderBy(f => f.Modified)
            .ToList();
    }

    public Stream OpenActivity(string name) => File.OpenRead(Path.Combine(_garmin, "Activity", name));

    public void WriteNewFile(string name, byte[] content)
    {
        var dir = Path.Combine(_garmin, "NewFiles");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, name), content);
    }

    public void Dispose()
    {
    }
}
