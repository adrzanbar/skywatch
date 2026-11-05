using Xunit;
using SkyWatch.Analytics;
using SkyWatch.Contracts;

namespace SkyWatch.Analytics.Tests;

public sealed class AircraftTrackerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Emergency_opens_once_and_closes()
    {
        var tracker = new AircraftTracker(new TrackerOptions());

        IReadOnlyList<FlightAlert> opened = tracker.Process(
            TestData.Position(squawk: "7600", emergency: "none", at: T0));

        FlightAlert alert = Assert.Single(opened);
        Assert.Equal(AlertKind.Emergency, alert.Kind);
        Assert.Equal(AlertStatus.Open, alert.Status);
        Assert.Equal(AlertSeverity.Critical, alert.Severity);

        Assert.Empty(tracker.Process(TestData.Position(squawk: "7600", at: T0.AddSeconds(10))));

        IReadOnlyList<FlightAlert> closed = tracker.Process(
            TestData.Position(squawk: "1200", at: T0.AddSeconds(20)));

        FlightAlert close = Assert.Single(closed);
        Assert.Equal(AlertStatus.Closed, close.Status);
        Assert.Equal(alert.AlertId, close.AlertId);
    }

    [Fact]
    public void Rapid_descent_opens_and_recovers()
    {
        var tracker = new AircraftTracker(new TrackerOptions());

        FlightAlert opened = Assert.Single(tracker.Process(
            TestData.Position(verticalRate: -3_500, at: T0)));
        Assert.Equal(AlertKind.RapidDescent, opened.Kind);

        FlightAlert closed = Assert.Single(tracker.Process(
            TestData.Position(verticalRate: -500, at: T0.AddSeconds(10))));
        Assert.Equal(AlertStatus.Closed, closed.Status);
    }

    [Fact]
    public void Signal_loss_opens_and_closes()
    {
        var tracker = new AircraftTracker(new TrackerOptions());

        FlightAlert opened = Assert.Single(tracker.Process(
            TestData.Position(seenPositionSeconds: 90, at: T0)));
        Assert.Equal(AlertKind.SignalLoss, opened.Kind);

        FlightAlert closed = Assert.Single(tracker.Process(
            TestData.Position(seenPositionSeconds: 5, at: T0.AddSeconds(10))));
        Assert.Equal(AlertStatus.Closed, closed.Status);
    }

    [Fact]
    public void Watchlist_match_opens_and_closes()
    {
        var tracker = new AircraftTracker(new TrackerOptions { Watchlist = ["ARG123"] });

        FlightAlert opened = Assert.Single(tracker.Process(
            TestData.Position(callsign: "ARG123", at: T0)));
        Assert.Equal(AlertKind.Watchlist, opened.Kind);

        FlightAlert closed = Assert.Single(tracker.Process(
            TestData.Position(callsign: "OTHER", at: T0.AddSeconds(10))));
        Assert.Equal(AlertStatus.Closed, closed.Status);
    }

    [Fact]
    public void Squawk_change_is_a_transient_pair()
    {
        var tracker = new AircraftTracker(new TrackerOptions());

        Assert.Empty(tracker.Process(TestData.Position(squawk: "1200", at: T0)));

        IReadOnlyList<FlightAlert> changes = tracker.Process(
            TestData.Position(squawk: "2000", at: T0.AddSeconds(10)));

        Assert.Equal(2, changes.Count);
        Assert.All(changes, alert => Assert.Equal(AlertKind.SquawkChange, alert.Kind));
        Assert.Contains(changes, alert => alert.Status == AlertStatus.Open);
        Assert.Contains(changes, alert => alert.Status == AlertStatus.Closed);
    }

    [Fact]
    public void Stale_aircraft_alerts_are_closed_and_state_is_removed()
    {
        var tracker = new AircraftTracker(new TrackerOptions { AircraftTimeoutMinutes = 10 });

        FlightAlert opened = Assert.Single(tracker.Process(
            TestData.Position(hex: "old", squawk: "7500", at: T0)));
        Assert.Equal(AlertStatus.Open, opened.Status);

        IReadOnlyList<FlightAlert> changes = tracker.Process(
            TestData.Position(hex: "new", at: T0.AddMinutes(11)));

        FlightAlert closed = Assert.Single(changes, alert => alert.Hex == "old");
        Assert.Equal(AlertStatus.Closed, closed.Status);
        Assert.Equal(opened.AlertId, closed.AlertId);
    }

    [Fact]
    public void Emergency_is_not_duplicated_while_active()
    {
        var tracker = new AircraftTracker(new TrackerOptions());

        Assert.Single(tracker.Process(TestData.Position(squawk: "7700", at: T0)));
        Assert.Empty(tracker.Process(TestData.Position(squawk: "7700", at: T0.AddSeconds(10))));
    }
}
