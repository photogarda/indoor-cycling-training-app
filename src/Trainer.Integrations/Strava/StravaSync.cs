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

    /// <summary>Imports rides since the last sync (first time: the last 90 days).</summary>
    public async Task<SyncReport> SyncAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var token = await AccessTokenAsync(ct);
        var athlete = service.GetAthlete();
        var after = (athlete.StravaLastSyncUtc ?? DateTime.UtcNow.AddDays(-90)).AddDays(-2);
        var list = await client.ListRidesAsync(token, after, ct);
        var rides = new List<(RideData, ActivitySource)>();
        var errors = new List<string>();
        var skipped = 0;
        foreach (var a in list)
        {
            if (service.HasStravaActivity(a.Id) || service.HasActivityNear(DateTime.SpecifyKind(a.StartUtc, DateTimeKind.Utc).ToLocalTime()))
            {
                skipped++;
                continue;
            }
            progress?.Report($"Downloading {a.Name} ({a.StartUtc.ToLocalTime():d MMM})…");
            try
            {
                rides.Add((await client.GetRideAsync(token, a, ct), ActivitySource.Strava));
            }
            catch (InvalidOperationException ex)
            {
                errors.Add($"{a.Name}: {ex.Message}");
                if (ex.Message.Contains("rate limit")) break;
            }
        }
        var results = service.ImportRides(rides);
        service.UpdateIntegrationSettings(x => x.StravaLastSyncUtc = DateTime.UtcNow);
        var report = SyncReport.From(results, errors);
        return report with { Duplicates = report.Duplicates + skipped };
    }
}
