namespace Trainer.Integrations;

/// <summary>
/// Watches a folder (e.g. where Garmin Connect downloads land) and imports any new FIT file.
/// Events are batched for a couple of seconds so a download that is still being written isn't read early.
/// </summary>
public sealed class FolderWatcher : IDisposable
{
    private readonly FitSync _sync;
    private readonly FileSystemWatcher _watcher;
    private readonly HashSet<string> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly Timer _timer;
    private readonly object _gate = new();

    public FolderWatcher(FitSync sync, string folder)
    {
        _sync = sync;
        Folder = folder;
        _watcher = new FileSystemWatcher(folder)
        {
            Filter = "*.*",
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            IncludeSubdirectories = false,
        };
        _watcher.Created += (_, e) => Queue(e.FullPath);
        _watcher.Renamed += (_, e) => Queue(e.FullPath);
        _watcher.Changed += (_, e) => Queue(e.FullPath);
        _timer = new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public string Folder { get; }

    /// <summary>Raised on a thread-pool thread after each batch.</summary>
    public event EventHandler<SyncReport>? Imported;

    public void Start(bool scanExisting = true)
    {
        if (scanExisting)
            foreach (var f in Directory.EnumerateFiles(Folder).Where(IsFit)) Queue(f);
        _watcher.EnableRaisingEvents = true;
    }

    private static bool IsFit(string path) => path.EndsWith(".fit", StringComparison.OrdinalIgnoreCase);

    private void Queue(string path)
    {
        if (!IsFit(path)) return;
        lock (_gate)
        {
            _pending.Add(path);
            _timer.Change(TimeSpan.FromSeconds(3), Timeout.InfiniteTimeSpan);
        }
    }

    private void Flush()
    {
        List<string> batch;
        lock (_gate)
        {
            batch = [.. _pending];
            _pending.Clear();
        }
        if (batch.Count == 0) return;
        try
        {
            var report = _sync.ImportFiles(batch.Where(File.Exists));
            if (report.Added + report.Replaced + report.Errors.Count > 0) Imported?.Invoke(this, report);
        }
        catch (Exception ex)
        {
            Imported?.Invoke(this, new SyncReport(0, 0, 0, [ex.Message]));
        }
    }

    public void Dispose()
    {
        _watcher.Dispose();
        _timer.Dispose();
    }
}
