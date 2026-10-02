using Trainer.Core.Models;
using Trainer.Core.Training;
using Trainer.Data.Services;

namespace Trainer.Integrations.Strava;

/// <summary>Pulls rides from Strava that aren't already imported (by Strava id or start time ±2 min).</summary>
public class StravaSync(TrainerService service, StravaClient client)
{
    public bool IsConnected
    {
        get
        {
            var a = service.GetAthlete();
            return !string.IsNullOrEmpty(a.StravaRefreshToken) && !string.IsNullOrEmpty(a.StravaClientId);
        }
    }

    public async Task ConnectAsync(string clientId, string clientSecret, Action<string>? openBrowser = null, CancellationToken ct = default)
    {
        var tokens = await client.AuthorizeAsync(clientId, clientSecret, openBrowser, ct);
        service.UpdateIntegrationSettings(a =>
        {
            a.StravaClientId = clientId;
            a.StravaClientSecret = clientSecret;
            Store(a, tokens);
        });
    }

    public void Disconnect() => service.UpdateIntegrationSettings(a =>
    {
        a.StravaAccessToken = null;
        a.StravaRefreshToken = null;
        a.StravaTokenExpiresUtc = null;
    });

    private static void Store(Athlete a, StravaTokens t)
    {
        a.StravaAccessToken = t.AccessToken;
        a.StravaRefreshToken = t.RefreshToken;
        a.StravaTokenExpiresUtc = t.ExpiresUtc;
    }

    private async Task<string> AccessTokenAsync(CancellationToken ct)
    {
        var a = service.GetAthlete();
        if (string.IsNullOrEmpty(a.StravaRefreshToken) || string.IsNullOrEmpty(a.StravaClientId) || string.IsNullOrEmpty(a.StravaClientSecret))
            throw new InvalidOperationException("Connect Strava in Settings first.");
        if (a.StravaAccessToken is not null && a.StravaTokenExpiresUtc > DateTime.UtcNow.AddMinutes(5)) return a.StravaAccessToken;
        var t = await client.RefreshAsync(a.StravaClientId, a.StravaClientSecret, a.StravaRefreshToken, ct);
        service.UpdateIntegrationSettings(x => Store(x, t));
        return t.AccessToken;
    }

    /// <summary>Rides are saved in batches so a long history sync keeps what it has if it stops.</summary>
    private const int Batch = 20;

    /// <summary>Waits out Strava's 15-minute limit. Replaceable in tests.</summary>
    public Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = Task.Delay;
    public Func<DateTime> UtcNow { get; init; } = () => DateTime.UtcNow;

    /// <summary>
    /// Imports rides from Strava. <see cref="StravaRange.NewRides"/>: since the last sync (first time: the last
    /// 90 days). The history ranges reach further back; rides already imported are skipped without asking Strava
    /// again, so an interrupted history sync simply continues where it stopped when run again.
    /// When Strava's 15-minute request limit is hit the sync waits for the next window and carries on; at the
    /// daily limit it stops and keeps what it downloaded.
    /// </summary>
    public async Task<SyncReport> SyncAsync(StravaRange range = StravaRange.NewRides, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var token = await AccessTokenAsync(ct);
        var athlete = service.GetAthlete();
        var after = range switch
        {
            StravaRange.LastYear => UtcNow().AddYears(-1),
            StravaRange.Everything => DateTime.UnixEpoch,
            _ => (athlete.StravaLastSyncUtc ?? UtcNow().AddDays(-90)).AddDays(-2),
        };
        var list = await WithRetry(() => client.ListRidesAsync(token, after, ct), progress, null, ct);

        var results = new List<ImportResult>();
        var pending = new List<(RideData, ActivitySource)>();
        var errors = new List<string>();
        var skipped = 0;
        string? note = null;
        DateTime? lastDoneUtc = null;
        var todo = list.Where(a =>
        {
            var known = service.HasStravaActivity(a.Id) || service.HasActivityNear(DateTime.SpecifyKind(a.StartUtc, DateTimeKind.Utc).ToLocalTime());
            if (known) skipped++;
            return !known;
        }).ToList();

        void Flush()
        {
            if (pending.Count == 0) return;
            results.AddRange(service.ImportRides(pending));
            pending.Clear();
        }

        for (var i = 0; i < todo.Count; i++)
        {
            var a = todo[i];
            var counter = todo.Count > 1 ? $" {i + 1} of {todo.Count}" : "";
            progress?.Report($"Downloading{counter}: {a.Name} ({a.StartUtc.ToLocalTime():d MMM yyyy})…");
            try
            {
                pending.Add((await WithRetry(() => client.GetRideAsync(token, a, ct), progress, $"{i} of {todo.Count} downloaded", ct), ActivitySource.Strava));
                lastDoneUtc = a.StartUtc;
            }
            catch (StravaRateLimitException ex)
            {
                note = $"Stopped at {i} of {todo.Count}: {ex.Message}";
                break;
            }
            catch (InvalidOperationException ex)
            {
                errors.Add($"{a.Name}: {ex.Message}");
                lastDoneUtc = a.StartUtc;
            }
            if (pending.Count >= Batch) Flush();
        }
        Flush();

        var finished = note is null;
        service.UpdateIntegrationSettings(x =>
        {
            if (finished) x.StravaLastSyncUtc = UtcNow();
            // A new-rides sync that stopped early resumes from the last ride it got. A history sync that stopped
            // leaves the marker alone: running it again skips what is already in.
            else if (range == StravaRange.NewRides && lastDoneUtc is { } d) x.StravaLastSyncUtc = d;
        });
        var report = SyncReport.From(results, errors);
        return report with { Duplicates = report.Duplicates + skipped, Note = note };
    }

    /// <summary>Runs a Strava request; on the 15-minute limit waits for the next window and tries again.</summary>
    private async Task<T> WithRetry<T>(Func<Task<T>> call, IProgress<string>? progress, string? done, CancellationToken ct)
    {
        for (var waits = 0; ; waits++)
        {
            try
            {
                return await call();
            }
            // Still refused after a few windows: Strava's daily count has run out even if the headers didn't say so.
            catch (StravaRateLimitException ex) when (!ex.Daily && waits >= 3)
            {
                throw new StravaRateLimitException(daily: true);
            }
            catch (StravaRateLimitException ex) when (!ex.Daily)
            {
                var now = UtcNow();
                var at = ex.RetryAtUtc(now);
                progress?.Report($"Strava allows only so many requests per 15 minutes. Waiting until {at.ToLocalTime():HH:mm} to continue" +
                                 (done is null ? "…" : $" ({done})…"));
                await Delay(at - now, ct);
            }
        }
    }
}

public enum StravaRange
{
    /// <summary>Since the last sync (first time: the last 90 days).</summary>
    NewRides,
    LastYear,
    Everything,
}
