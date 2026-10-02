using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Trainer.Core.Training;

namespace Trainer.Integrations.Strava;

public record StravaTokens(string AccessToken, string RefreshToken, DateTime ExpiresUtc);

public record StravaActivitySummary(long Id, string Name, string SportType, DateTime StartUtc, int MovingTime, double Distance);

/// <summary>
/// Strava answered 429. Strava counts requests per 15 minutes (windows start at :00, :15, :30 and :45) and per
/// day (resets at midnight UTC). <see cref="Daily"/> tells which one ran out.
/// </summary>
public sealed class StravaRateLimitException(bool daily) : InvalidOperationException(daily
    ? "Strava's daily request limit is reached. Sync again tomorrow to continue."
    : "Strava rate limit reached. Try again in 15 minutes.")
{
    public bool Daily { get; } = daily;

    /// <summary>When requests are allowed again: the next quarter hour, or midnight UTC for the daily limit.</summary>
    public DateTime RetryAtUtc(DateTime nowUtc)
    {
        if (Daily) return nowUtc.Date.AddDays(1);
        var quarter = nowUtc.Date.AddMinutes((int)(nowUtc.TimeOfDay.TotalMinutes / 15) * 15);
        return quarter.AddMinutes(15).AddSeconds(10);
    }

    /// <summary>
    /// Reads Strava's "X-RateLimit-Limit: 200,2000" and "X-RateLimit-Usage: 201,1500" headers (and the
    /// X-ReadRateLimit pair): the limit is daily when a daily count has reached its cap.
    /// </summary>
    public static StravaRateLimitException FromHeaders(Func<string, string?> header)
    {
        static (int, int)? Pair(string? v)
        {
            var parts = v?.Split(',');
            return parts is { Length: 2 } && int.TryParse(parts[0].Trim(), out var a) && int.TryParse(parts[1].Trim(), out var b) ? (a, b) : null;
        }
        var daily = false;
        foreach (var prefix in new[] { "X-RateLimit", "X-ReadRateLimit" })
        {
            if (Pair(header($"{prefix}-Limit")) is { } limit && Pair(header($"{prefix}-Usage")) is { } usage && usage.Item2 >= limit.Item2)
                daily = true;
        }
        return new StravaRateLimitException(daily);
    }
}

/// <summary>
/// Minimal Strava API client for your own API application (single athlete). Register an app at
/// strava.com/settings/api with "Authorization Callback Domain" set to <c>localhost</c>.
/// </summary>
public sealed class StravaClient(HttpClient http)
{
    public const int CallbackPort = 8723;
    public static string RedirectUri => $"http://localhost:{CallbackPort}/strava/callback/";
    private const string Api = "https://www.strava.com/api/v3/";
    private static readonly HashSet<string> RideTypes = new(StringComparer.OrdinalIgnoreCase)
        { "Ride", "VirtualRide", "GravelRide", "MountainBikeRide", "EBikeRide", "EMountainBikeRide", "Velomobile", "Handcycle" };

    public static string AuthorizeUrl(string clientId) =>
        $"https://www.strava.com/oauth/authorize?client_id={Uri.EscapeDataString(clientId)}&response_type=code" +
        $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}&approval_prompt=auto&scope=read,activity:read_all";

    /// <summary>
    /// Opens the browser for consent and waits for Strava to redirect back to a local listener with the code.
    /// </summary>
    public async Task<StravaTokens> AuthorizeAsync(string clientId, string clientSecret, Action<string>? openBrowser = null, CancellationToken ct = default)
    {
        using var listener = new HttpListener();
        listener.Prefixes.Add(RedirectUri);
        listener.Start();
        var url = AuthorizeUrl(clientId);
        (openBrowser ?? (u => Process.Start(new ProcessStartInfo(u) { UseShellExecute = true })))(url);

        using var reg = ct.Register(listener.Stop);
        var ctx = await listener.GetContextAsync().WaitAsync(TimeSpan.FromMinutes(5), ct);
        var code = ctx.Request.QueryString["code"];
        var error = ctx.Request.QueryString["error"];
        var html = code is not null
            ? "<html><body style='font-family:sans-serif'><h2>Strava connected.</h2>You can close this tab and go back to the app.</body></html>"
            : $"<html><body style='font-family:sans-serif'><h2>Strava was not connected.</h2>{WebUtility.HtmlEncode(error)}</body></html>";
        var bytes = Encoding.UTF8.GetBytes(html);
        ctx.Response.ContentType = "text/html; charset=utf-8";
        await ctx.Response.OutputStream.WriteAsync(bytes, ct);
        ctx.Response.Close();
        if (code is null) throw new InvalidOperationException($"Strava authorisation failed: {error ?? "no code returned"}.");

        return await TokenAsync(new Dictionary<string, string>
        {
            ["client_id"] = clientId, ["client_secret"] = clientSecret, ["code"] = code, ["grant_type"] = "authorization_code",
        }, ct);
    }

    public Task<StravaTokens> RefreshAsync(string clientId, string clientSecret, string refreshToken, CancellationToken ct = default) =>
        TokenAsync(new Dictionary<string, string>
        {
            ["client_id"] = clientId, ["client_secret"] = clientSecret, ["refresh_token"] = refreshToken, ["grant_type"] = "refresh_token",
        }, ct);

    private async Task<StravaTokens> TokenAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        using var res = await http.PostAsync("https://www.strava.com/oauth/token", new FormUrlEncodedContent(form), ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"Strava token request failed ({(int)res.StatusCode}): {body}");
        var t = JsonSerializer.Deserialize<TokenResponse>(body)!;
        return new StravaTokens(t.AccessToken, t.RefreshToken, DateTimeOffset.FromUnixTimeSeconds(t.ExpiresAt).UtcDateTime);
    }

    /// <summary>Rides started after <paramref name="afterUtc"/>, oldest first.</summary>
    public async Task<List<StravaActivitySummary>> ListRidesAsync(string accessToken, DateTime afterUtc, CancellationToken ct = default)
    {
        var list = new List<StravaActivitySummary>();
        var after = new DateTimeOffset(DateTime.SpecifyKind(afterUtc, DateTimeKind.Utc)).ToUnixTimeSeconds();
        for (var page = 1; page < 500; page++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{Api}athlete/activities?after={after}&per_page=100&page={page}");
            req.Headers.Authorization = new("Bearer", accessToken);
            using var res = await http.SendAsync(req, ct);
            await EnsureOk(res, ct);
            var items = await res.Content.ReadFromJsonAsync<List<ActivityJson>>(cancellationToken: ct) ?? [];
            if (items.Count == 0) break;
            list.AddRange(items.Where(a => RideTypes.Contains(a.SportType ?? a.Type ?? ""))
                .Select(a => new StravaActivitySummary(a.Id, a.Name ?? "Ride", a.SportType ?? a.Type ?? "Ride", a.StartDate, a.MovingTime, a.Distance)));
            if (items.Count < 100) break;
        }
        return list.OrderBy(a => a.StartUtc).ToList();
    }

    /// <summary>Downloads power, heart rate and cadence streams and turns them into a ride.</summary>
    public async Task<RideData> GetRideAsync(string accessToken, StravaActivitySummary a, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{Api}activities/{a.Id}/streams?keys=time,watts,heartrate,cadence&key_by_type=true");
        req.Headers.Authorization = new("Bearer", accessToken);
        using var res = await http.SendAsync(req, ct);
        await EnsureOk(res, ct);
        var json = await res.Content.ReadAsStringAsync(ct);
        return FromStreams(a, json);
    }

    /// <summary>Builds a ride from a <c>key_by_type=true</c> streams response.</summary>
    public static RideData FromStreams(StravaActivitySummary a, string streamsJson)
    {
        using var doc = JsonDocument.Parse(streamsJson);
        double?[] Read(string key)
        {
            if (!doc.RootElement.TryGetProperty(key, out var s) || !s.TryGetProperty("data", out var data)) return [];
            return data.EnumerateArray().Select(v => v.ValueKind == JsonValueKind.Number ? v.GetDouble() : (double?)null).ToArray();
        }
        var time = Read("time");
        var watts = Read("watts");
        var hr = Read("heartrate");
        var cad = Read("cadence");
        var start = DateTime.SpecifyKind(a.StartUtc, DateTimeKind.Utc).ToLocalTime();
        var ride = new RideData
        {
            Name = a.Name,
            StartTime = start,
            TimerSeconds = a.MovingTime > 0 ? a.MovingTime : null,
            DistanceKm = a.Distance > 0 ? a.Distance / 1000 : null,
            StravaId = a.Id,
        };
        for (var i = 0; i < time.Length; i++)
        {
            ride.Samples.Add(new RideSample(start.AddSeconds(time[i] ?? i),
                i < watts.Length ? watts[i] : null, i < hr.Length ? hr[i] : null, i < cad.Length ? cad[i] : null));
        }
        return ride;
    }

    private static async Task EnsureOk(HttpResponseMessage res, CancellationToken ct)
    {
        if (res.IsSuccessStatusCode) return;
        if (res.StatusCode == HttpStatusCode.TooManyRequests)
            throw StravaRateLimitException.FromHeaders(h => res.Headers.TryGetValues(h, out var v) ? v.FirstOrDefault() : null);
        var body = await res.Content.ReadAsStringAsync(ct);
        throw new InvalidOperationException($"Strava request failed ({(int)res.StatusCode}): {body}");
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")] public string AccessToken { get; set; } = "";
        [JsonPropertyName("refresh_token")] public string RefreshToken { get; set; } = "";
        [JsonPropertyName("expires_at")] public long ExpiresAt { get; set; }
    }

    private sealed class ActivityJson
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("sport_type")] public string? SportType { get; set; }
        [JsonPropertyName("start_date")] public DateTime StartDate { get; set; }
        [JsonPropertyName("moving_time")] public int MovingTime { get; set; }
        [JsonPropertyName("distance")] public double Distance { get; set; }
    }
}
