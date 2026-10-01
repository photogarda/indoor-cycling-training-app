namespace Trainer.Integrations.Edge;

public record EdgeFile(string Name, long Size, DateTime Modified);

/// <summary>
/// A Garmin Edge (or any Garmin device) seen over USB. Older Edges mount as a drive letter; newer ones
/// connect as MTP devices. Both expose the same two folders the app needs.
/// </summary>
public interface IEdgeDevice : IDisposable
{
    string DisplayName { get; }

    /// <summary>FIT files in <c>Garmin/Activity</c>.</summary>
    IReadOnlyList<EdgeFile> ListActivities();

    Stream OpenActivity(string name);

    /// <summary>Copies a file into <c>Garmin/NewFiles</c>; the Edge imports it when unplugged.</summary>
    void WriteNewFile(string name, byte[] content);
}
