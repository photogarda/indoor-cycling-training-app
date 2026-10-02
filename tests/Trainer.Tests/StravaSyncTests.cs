using System.Net;
using System.Text;
using Trainer.Integrations.Strava;

namespace Trainer.Tests;

public class StravaSyncTests
{
    private static readonly DateTime Now = new(2026, 10, 1, 9, 5, 0, DateTimeKind.Utc);

    /// <summary>Fake Strava API: a list of rides, streams per ride, and scripted 429 answers.</summary>
    private sealed class FakeStrava : HttpMessageHandler
    {
        public List<(long Id, DateTime Start)> Rides { get; } = [];
        /// <summary>Stream requests (counted from 1) that get a 429, with whether it is the daily limit.</summary>
        public Dictionary<int, bool> Limits { get; } = [];
        public List<string> Requests { get; } = [];
        private int _streams;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            Requests.Add(url);
            if (url.Contains("athlete/activities"))
            {
                var after = long.Parse(url.Split("after=")[1].Split('&')[0]);
                var page = int.Parse(url.Split("&page=")[1]);
                var items = page == 1
                    ? Rides.Where(r => new DateTimeOffset(r.Start).ToUnixTimeSeconds() > after)
                        .Select(r => $$"""{"id":{{r.Id}},"name":"Ride {{r.Id}}","sport_type":"Ride","start_date":"{{r.Start:yyyy-MM-ddTHH:mm:ssZ}}","moving_time":600,"distance":5000}""")
                    : [];
                return Json("[" + string.Join(",", items) + "]");
            }
            _streams++;
            if (Limits.TryGetValue(_streams, out var daily))
            {
                var res = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                res.Headers.Add("X-RateLimit-Limit", "200,2000");
                res.Headers.Add("X-RateLimit-Usage", daily ? "120,2000" : "200,900");
                return Task.FromResult(res);
            }
            var secs = string.Join(",", Enumerable.Range(0, 600));
            var watts = string.Join(",", Enumerable.Repeat(200, 600));
            return Json("{\"time\":{\"data\":[" + secs + "]},\"watts\":{\"data\":[" + watts + "]}}");
        }

        private static Task<HttpResponseMessage> Json(string body) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }

    private static (StravaSync Sync, List<TimeSpan> Waits) Create(TestDb t, FakeStrava fake)
    {
        t.Service.UpdateIntegrationSettings(a =>
        {
            a.StravaClientId = "1"; a.StravaClientSecret = "s"; a.StravaRefreshToken = "r";
            a.StravaAccessToken = "a"; a.StravaTokenExpiresUtc = DateTime.UtcNow.AddHours(1);
        });
        var waits = new List<TimeSpan>();
        var sync = new StravaSync(t.Service, new StravaClient(new HttpClient(fake)))
        {
            UtcNow = () => Now,
            Delay = (d, _) => { waits.Add(d); return Task.CompletedTask; },
        };
        return (sync, waits);
    }

    private static FakeStrava YearOfRides()
    {
        var fake = new FakeStrava();
        // One ride every ~3 weeks over the past 14 months: some are older than a year.
        for (var i = 1; i <= 20; i++) fake.Rides.Add((i, Now.AddDays(-21 * i).Date.AddHours(17)));
        return fake;
    }

    [Fact]
    public async Task Last_12_months_imports_rides_older_than_the_first_sync_window()
    {
        using var t = new TestDb();
        var fake = YearOfRides();
        var (sync, _) = Create(t, fake);

        var report = await sync.SyncAsync(StravaRange.LastYear);

        var inYear = fake.Rides.Count(r => r.Start > Now.AddYears(-1));
        Assert.Equal(inYear, report.Added);
        Assert.True(inYear > 90 / 21 + 1, "the year reaches well past the default 90 days");
        Assert.Equal(inYear, t.Service.GetActivities().Count);
        Assert.Null(report.Note);
        Assert.Equal(Now, t.Service.GetAthlete().StravaLastSyncUtc);
    }

    [Fact]
    public async Task All_rides_reaches_back_to_the_start()
    {
        using var t = new TestDb();
        var fake = YearOfRides();
        var (sync, _) = Create(t, fake);

        var report = await sync.SyncAsync(StravaRange.Everything);

        Assert.Equal(fake.Rides.Count, report.Added);
        Assert.Contains(fake.Requests, u => u.Contains("after=0&"));
    }

    [Fact]
    public async Task The_15_minute_limit_waits_for_the_next_window_and_carries_on()
    {
        using var t = new TestDb();
        var fake = YearOfRides();
        fake.Limits[3] = false;
        var (sync, waits) = Create(t, fake);

        var report = await sync.SyncAsync(StravaRange.Everything);

        Assert.Equal(fake.Rides.Count, report.Added);
        Assert.Empty(report.Errors);
        // 09:05 → next window at 09:15 (plus a few seconds' margin).
        var wait = Assert.Single(waits);
        Assert.InRange(wait.TotalMinutes, 10, 10.5);
    }

    [Fact]
    public async Task The_daily_limit_stops_keeps_what_it_has_and_the_next_run_continues()
    {
        using var t = new TestDb();
        var fake = YearOfRides();
        fake.Limits[8] = true;
        var (sync, waits) = Create(t, fake);

        var first = await sync.SyncAsync(StravaRange.Everything);

        Assert.Equal(7, first.Added);
        Assert.NotNull(first.Note);
        Assert.Contains("daily", first.Note);
        Assert.Empty(waits);
        Assert.Equal(7, t.Service.GetActivities().Count);
        Assert.Null(t.Service.GetAthlete().StravaLastSyncUtc); // history sync didn't finish

        var streamsBefore = fake.Requests.Count(u => u.Contains("/streams"));
        var second = await sync.SyncAsync(StravaRange.Everything);

        Assert.Equal(fake.Rides.Count - 7, second.Added);
        Assert.Equal(7, second.Duplicates);
        // Rides already in weren't downloaded again.
        Assert.Equal(fake.Rides.Count - 7, fake.Requests.Count(u => u.Contains("/streams")) - streamsBefore);
        Assert.Equal(fake.Rides.Count, t.Service.GetActivities().Count);
    }

    [Fact]
    public void Rate_limit_headers_tell_daily_from_15_minutes()
    {
        Assert.False(StravaRateLimitException.FromHeaders(h => h switch
        {
            "X-RateLimit-Limit" => "200,2000", "X-RateLimit-Usage" => "201,900", _ => null,
        }).Daily);
        Assert.True(StravaRateLimitException.FromHeaders(h => h switch
        {
            "X-ReadRateLimit-Limit" => "100,1000", "X-ReadRateLimit-Usage" => "40,1000", _ => null,
        }).Daily);
        Assert.Equal(new DateTime(2026, 10, 2), new StravaRateLimitException(true).RetryAtUtc(Now));
    }
}
