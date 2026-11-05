using Xunit;
using SkyWatch.Contracts;

namespace SkyWatch.Analytics.Tests;

internal static class TestData
{
    public static AircraftPosition Position(
        string hex = "abc123",
        DateTimeOffset? at = null,
        string? callsign = "ARG123",
        string? registration = "LV-ABC",
        string? aircraftType = "B738",
        double latitude = -34.6,
        double longitude = -58.4,
        int? altitude = 38_000,
        double? speed = 450,
        double? track = 90,
        double? verticalRate = 0,
        string? squawk = "1200",
        string? emergency = "none",
        bool onGround = false,
        double? seenPositionSeconds = 1,
        bool military = false)
    {
        DateTimeOffset snapshotAt = at ?? DateTimeOffset.UnixEpoch;

        return new AircraftPosition
        {
            EventId = $"{hex}:{snapshotAt.ToUnixTimeMilliseconds()}",
            SnapshotAt = snapshotAt,
            Source = "test",
            Zone = "test",
            Hex = hex,
            Callsign = callsign,
            Registration = registration,
            AircraftType = aircraftType,
            Latitude = latitude,
            Longitude = longitude,
            AltitudeBaroFt = altitude,
            AltitudeGeomFt = altitude,
            GroundSpeedKt = speed,
            TrackDeg = track,
            VerticalRateFpm = verticalRate,
            Squawk = squawk,
            Emergency = emergency,
            OnGround = onGround,
            SeenPositionSeconds = seenPositionSeconds,
            Military = military
        };
    }
}
