using Xunit;
using SkyWatch.Analytics;
using SkyWatch.Contracts;

namespace SkyWatch.Analytics.Tests;

public sealed class AirportAggregatorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static AirportOptions Options() => new()
    {
        Airports =
        [
            new AirportConfig
            {
                Code = "TST",
                Name = "Test Airport",
                Latitude = 0,
                Longitude = 0,
                RadiusNm = 30
            }
        ],
        HoldingWindowSeconds = 120
    };

    [Fact]
    public void Counts_airport_traffic_and_fleet()
    {
        var aggregator = new AirportAggregator(Options());

        aggregator.Process(TestData.Position(
            hex: "ground", callsign: "ARG123", latitude: 0.01, longitude: 0.01,
            altitude: 0, speed: 10, onGround: true, at: T0));

        aggregator.Process(TestData.Position(
            hex: "approach", callsign: "LPE456", latitude: 0.02, longitude: 0.02,
            altitude: 5_000, speed: 200, verticalRate: -1_000, at: T0));

        aggregator.Process(TestData.Position(
            hex: "departure", callsign: "DAL789", latitude: 0.03, longitude: 0.03,
            altitude: 6_000, speed: 250, verticalRate: 1_500, at: T0));

        aggregator.Process(TestData.Position(
            hex: "holding", callsign: "GLO101", latitude: 0.04, longitude: 0.04,
            altitude: 8_000, speed: 220, verticalRate: 0, track: 0, at: T0));
        aggregator.Process(TestData.Position(
            hex: "holding", callsign: "GLO101", latitude: 0.04, longitude: 0.04,
            altitude: 8_000, speed: 220, verticalRate: 0, track: 90, at: T0.AddSeconds(30)));
        aggregator.Process(TestData.Position(
            hex: "holding", callsign: "GLO101", latitude: 0.04, longitude: 0.04,
            altitude: 8_000, speed: 220, verticalRate: 0, track: 180, at: T0.AddSeconds(60)));
        aggregator.Process(TestData.Position(
            hex: "holding", callsign: "GLO101", latitude: 0.04, longitude: 0.04,
            altitude: 8_000, speed: 220, verticalRate: 0, track: 270, at: T0.AddSeconds(90)));
        aggregator.Process(TestData.Position(
            hex: "holding", callsign: "GLO101", latitude: 0.04, longitude: 0.04,
            altitude: 8_000, speed: 220, verticalRate: 0, track: 360, at: T0.AddSeconds(120)));

        aggregator.Process(TestData.Position(
            hex: "outside", callsign: "XXX999", latitude: 1, longitude: 1,
            altitude: 38_000, speed: 450, military: true, emergency: "general", at: T0));

        AirportAggregationResult result = aggregator.Snapshot(T0.AddSeconds(120));

        AirportCongestion airport = Assert.Single(result.Airports);
        Assert.Equal("TST", airport.AirportCode);
        Assert.Equal(4, airport.AircraftInRange);
        Assert.Equal(1, airport.OnGround);
        Assert.Equal(1, airport.ApproachQueue);
        Assert.Equal(1, airport.Departures);
        Assert.Equal(1, airport.Holdings);

        FleetStats fleet = result.Fleet;
        Assert.Equal(5, fleet.Aircraft);
        Assert.Equal(1, fleet.Military);
        Assert.Equal(1, fleet.Emergency);
        Assert.Equal(1, fleet.OnGround);
        Assert.Equal(1, fleet.ByAirline["Aerolineas Argentinas"]);
        Assert.Equal(1, fleet.ByAirline["LATAM Peru"]);
        Assert.Equal(1, fleet.ByAirline["Delta"]);
        Assert.Equal(1, fleet.ByAirline["Gol"]);
        Assert.Equal(1, fleet.ByAirline["Other"]);
        Assert.Equal(5, fleet.ByType["B738"]);
        Assert.Equal(1, fleet.ByAltitudeBand["ground"]);
        Assert.Equal(3, fleet.ByAltitudeBand["0-10k"]);
        Assert.Equal(1, fleet.ByAltitudeBand["30-40k"]);
    }

    [Fact]
    public void Stale_aircraft_are_removed()
    {
        var aggregator = new AirportAggregator(Options());
        aggregator.Process(TestData.Position(hex: "old", at: T0));

        AirportAggregationResult result = aggregator.Snapshot(T0.AddMinutes(11));

        Assert.Equal(0, result.Fleet.Aircraft);
        Assert.Equal(0, Assert.Single(result.Airports).AircraftInRange);
    }
}
