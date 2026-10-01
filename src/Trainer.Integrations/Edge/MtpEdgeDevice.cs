#if WINDOWS
using MediaDevices;

namespace Trainer.Integrations.Edge;

/// <summary>A newer Edge that connects over MTP (Windows Portable Devices) instead of as a drive letter.</summary>
public sealed class MtpEdgeDevice : IEdgeDevice
{
    private readonly MediaDevice _device;
    private readonly string _garmin;

    private MtpEdgeDevice(MediaDevice device, string garmin)
    {
        _device = device;
        _garmin = garmin;
    }

    public string DisplayName => $"{_device.FriendlyName} (MTP)";

    /// <summary>Connected Garmin MTP devices that have a Garmin folder in one of their storages.</summary>
    public static List<IEdgeDevice> FindAll()
    {
        var found = new List<IEdgeDevice>();
        var manager = MediaDeviceManager.Instance;
        if (manager is null) return found;
        foreach (var d in manager.GetDevices())
        {
            try
            {
                var label = $"{d.FriendlyName} {d.Description} {d.Manufacturer}";
                if (!label.Contains("garmin", StringComparison.OrdinalIgnoreCase) && !label.Contains("edge", StringComparison.OrdinalIgnoreCase))
                    continue;
                d.Connect(MediaDeviceAccess.Default, MediaDeviceShare.Default, false);
                var garmin = d.GetDirectories(@"\")
                    .Select(storage => storage.TrimEnd('\\') + @"\Garmin")
                    .FirstOrDefault(d.DirectoryExists);
                if (garmin is null)
                {
                    d.Disconnect();
                    continue;
                }
                found.Add(new MtpEdgeDevice(d, garmin));
            }
            catch (Exception)
            {
                // Device busy or locked: skip it; the user can retry or use a drive letter / folder.
            }
        }
        return found;
    }

    public IReadOnlyList<EdgeFile> ListActivities()
    {
        var dir = _garmin + @"\Activity";
        if (!_device.DirectoryExists(dir)) return [];
        return _device.GetFiles(dir)
            .Where(f => f.EndsWith(".fit", StringComparison.OrdinalIgnoreCase))
            .Select(f =>
            {
                var info = _device.GetFileInfo(f);
                return new EdgeFile(Path.GetFileName(f), (long)info.Length, info.LastWriteTime ?? info.CreationTime ?? DateTime.MinValue);
            })
            .OrderBy(f => f.Modified)
            .ToList();
    }

    public Stream OpenActivity(string name)
    {
        var ms = new MemoryStream();
        _device.DownloadFile(_garmin + @"\Activity\" + name, ms);
        ms.Position = 0;
        return ms;
    }

    public void WriteNewFile(string name, byte[] content)
    {
        var dir = _garmin + @"\NewFiles";
        if (!_device.DirectoryExists(dir)) _device.CreateDirectory(dir);
        var target = dir + @"\" + name;
        if (_device.FileExists(target)) _device.DeleteFile(target);
        using var ms = new MemoryStream(content);
        _device.UploadFile(ms, target);
    }

    public void Dispose()
    {
        if (_device.IsConnected) _device.Disconnect();
        _device.Dispose();
    }
}
#endif
