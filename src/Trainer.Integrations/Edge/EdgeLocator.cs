namespace Trainer.Integrations.Edge;

/// <summary>Finds a plugged-in Edge: a configured path first, then drive letters, then MTP devices.</summary>
public static class EdgeLocator
{
    public static List<IEdgeDevice> FindAll(string? configuredPath = null)
    {
        var list = new List<IEdgeDevice>();
        if (!string.IsNullOrWhiteSpace(configuredPath) && DriveEdgeDevice.LooksLikeGarmin(configuredPath))
            list.Add(new DriveEdgeDevice(configuredPath));

        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady || drive.DriveType is not (DriveType.Removable or DriveType.Fixed)) continue;
                var root = drive.RootDirectory.FullName;
                if (list.OfType<DriveEdgeDevice>().Any(d => d.Root.StartsWith(root, StringComparison.OrdinalIgnoreCase))) continue;
                // Only look one level down for a Garmin folder; skip the system drive.
                if (string.Equals(root, Path.GetPathRoot(Environment.SystemDirectory), StringComparison.OrdinalIgnoreCase) || root == "/") continue;
                if (Directory.Exists(Path.Combine(root, "Garmin", "Activity")) || Directory.Exists(Path.Combine(root, "Garmin", "NewFiles")))
                    list.Add(new DriveEdgeDevice(root));
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        // macOS mounts USB drives under /Volumes (e.g. /Volumes/GARMIN); Linux under /media or /run/media.
        if (!OperatingSystem.IsWindows())
        {
            var user = Environment.UserName;
            foreach (var mountRoot in new[] { "/Volumes", $"/media/{user}", $"/run/media/{user}", "/media" })
            {
                if (!Directory.Exists(mountRoot)) continue;
                foreach (var dir in SafeDirectories(mountRoot))
                {
                    if (list.OfType<DriveEdgeDevice>().Any(d => string.Equals(d.Root.TrimEnd('/'), dir.TrimEnd('/'), StringComparison.Ordinal))) continue;
                    if (Directory.Exists(Path.Combine(dir, "Garmin", "Activity")) || Directory.Exists(Path.Combine(dir, "Garmin", "NewFiles")))
                        list.Add(new DriveEdgeDevice(dir));
                }
            }
        }

#if WINDOWS
        if (list.Count == 0) list.AddRange(MtpEdgeDevice.FindAll());
#endif
        return list;
    }

    private static IEnumerable<string> SafeDirectories(string root)
    {
        try
        {
            return Directory.GetDirectories(root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
