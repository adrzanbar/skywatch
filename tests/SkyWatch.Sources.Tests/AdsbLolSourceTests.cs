using Xunit;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SkyWatch.Sources.AdsbLol;

namespace SkyWatch.Sources.Tests;

public sealed class AdsbLolSourceTests
{
    [Fact]
    public async Task Poll_deduplicates_aircraft_and_merges_emergency_and_military_flags()
    {
        var options = new AdsbLolOptions
        {
            Zones = [new WatchZone { Name = "test-zone", Latitude = 0, Longitude = 0, RadiusNm = 100 }],
            RequestSpacingMs = 0,
            IncludeMilitary = true,
            IncludeEmergencySquawks = true,
            EmergencySquawks = ["7600"]
        };

        var handler = new FakeAdsbHandler();
        var source = new AdsbLolSource(
            new HttpClient(handler) { BaseAddress = new Uri("https://adsb.test/") },
            Options.Create(options),
            NullLogger<AdsbLolSource>.Instance);

        AircraftSnapshot snapshot = await source.PollOnceAsync(CancellationToken.None);

        var position = Assert.Single(snapshot.Positions);
        Assert.Equal("abc123", position.Hex);
        Assert.Equal("ARG123", position.Callsign);
        Assert.Equal("LV-ABC", position.Registration);
        Assert.Equal("B738", position.AircraftType);
        Assert.Equal(38_000, position.AltitudeBaroFt);
        Assert.Equal(450, position.GroundSpeedKt);
        Assert.Equal(90, position.TrackDeg);
        Assert.Equal("7600", position.Squawk);
        Assert.Equal("general", position.Emergency);
        Assert.True(position.IsEmergency);
        Assert.True(position.Military);
        Assert.False(position.OnGround);
        Assert.Equal(snapshot.SnapshotAt, position.SnapshotAt);
    }

    [Fact]
    public async Task Ground_altitude_is_mapped_to_on_ground()
    {
        var options = new AdsbLolOptions
        {
            Zones = [new WatchZone { Name = "test-zone", Latitude = 0, Longitude = 0, RadiusNm = 100 }],
            RequestSpacingMs = 0,
            IncludeMilitary = false,
            IncludeEmergencySquawks = false
        };

        var handler = new FakeAdsbHandler(ground: true);
        var source = new AdsbLolSource(
            new HttpClient(handler) { BaseAddress = new Uri("https://adsb.test/") },
            Options.Create(options),
            NullLogger<AdsbLolSource>.Instance);

        AircraftSnapshot snapshot = await source.PollOnceAsync(CancellationToken.None);

        var position = Assert.Single(snapshot.Positions);
        Assert.True(position.OnGround);
        Assert.Equal(0, position.AltitudeBaroFt);
        Assert.False(position.IsEmergency);
        Assert.False(position.Military);
    }

    [Fact]
    public async Task Failing_auxiliary_endpoints_do_not_break_the_snapshot()
    {
        var options = new AdsbLolOptions
        {
            Zones = [new WatchZone { Name = "test-zone", Latitude = 0, Longitude = 0, RadiusNm = 100 }],
            RequestSpacingMs = 0,
            IncludeMilitary = true,
            IncludeEmergencySquawks = true,
            EmergencySquawks = ["7700"]
        };

        var handler = new FakeAdsbHandler(failAuxiliary: true);
        var source = new AdsbLolSource(
            new HttpClient(handler) { BaseAddress = new Uri("https://adsb.test/") },
            Options.Create(options),
            NullLogger<AdsbLolSource>.Instance);

        AircraftSnapshot snapshot = await source.PollOnceAsync(CancellationToken.None);

        Assert.Single(snapshot.Positions);
    }

    private sealed class FakeAdsbHandler(
        bool ground = false,
        bool failAuxiliary = false) : HttpMessageHandler
    {
        private const long Now = 1_793_913_528_000;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.AbsolutePath;

            if (path.Contains("/v2/point/", StringComparison.Ordinal))
            {
                return Task.FromResult(Json(AircraftJson(
                    squawk: "1200",
                    emergency: "none",
                    dbFlags: 0,
                    ground: ground)));
            }

            if (failAuxiliary)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            }

            if (path.EndsWith("/v2/mil", StringComparison.Ordinal))
            {
                return Task.FromResult(Json(AircraftJson(
                    squawk: "1200",
                    emergency: "none",
                    dbFlags: 1,
                    ground: ground)));
            }

            if (path.Contains("/v2/sqk/", StringComparison.Ordinal))
            {
                return Task.FromResult(Json(AircraftJson(
                    squawk: "7600",
                    emergency: "general",
                    dbFlags: 0,
                    ground: ground)));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static string AircraftJson(
            string squawk,
            string emergency,
            int dbFlags,
            bool ground) =>
            $$"""
            {
              "ac": [
                {
                  "hex": "abc123",
                  "flight": "ARG123   ",
                  "r": "LV-ABC",
                  "t": "B738",
                  "category": "A3",
                  "alt_baro": {{(ground ? "\"ground\"" : "38000")}},
                  "alt_geom": {{(ground ? "\"ground\"" : "38500")}},
                  "gs": {{(ground ? "10" : "450")}},
                  "track": 90,
                  "geom_rate": 0,
                  "squawk": "{{squawk}}",
                  "emergency": "{{emergency}}",
                  "lat": -34.6,
                  "lon": -58.4,
                  "seen_pos": 1,
                  "dbFlags": {{dbFlags}}
                }
              ],
              "now": {{Now}},
              "total": 1
            }
            """;

        private static HttpResponseMessage Json(string json) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
    }
}
