using System.Net.Http;
using Trainer.Data;
using Trainer.Data.Services;
using Trainer.Integrations;
using Trainer.Integrations.Strava;

namespace Trainer.App.Infrastructure;

/// <summary>Everything the screens share. Built once at start-up.</summary>
public sealed class AppServices : IDisposable
{
    private FolderWatcher? _watcher;

    private AppServices(TrainerService trainer)
    {
        Trainer = trainer;
        Fit = new FitSync(trainer);
        Strava = new StravaSync(trainer, new StravaClient(new HttpClient { Timeout = TimeSpan.FromSeconds(60) }));
    }

    public TrainerService Trainer { get; }
    public FitSync Fit { get; }
    public StravaSync Strava { get; }

    /// <summary>Raised (on a worker thread) when the watched folder imported rides.</summary>
    public event EventHandler<SyncReport>? FolderImported;

    public static AppServices Create()
    {
        var paths = new DataPaths();
        paths.Ensure();
        var trainer = new TrainerService(paths);
        trainer.Initialize();
        trainer.Refresh(); // a new day: mark yesterday's rides, regenerate from today
        var services = new AppServices(trainer);
        services.RestartWatcher();
        return services;
    }

    /// <summary>(Re)starts the watched folder from the current settings.</summary>
    public string? RestartWatcher()
    {
        _watcher?.Dispose();
        _watcher = null;
        var folder = Trainer.GetAthlete().WatchFolder;
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return null;
        _watcher = new FolderWatcher(Fit, folder);
        _watcher.Imported += (_, r) => FolderImported?.Invoke(this, r);
        _watcher.Start();
        return folder;
    }

    public void Dispose() => _watcher?.Dispose();
}
