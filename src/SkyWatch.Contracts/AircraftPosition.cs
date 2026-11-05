using System.Text.Json.Serialization;

namespace SkyWatch.Contracts;

/// <summary>A single aircraft state observed in one adsb.lol snapshot.</summary>
public sealed record AircraftPosition
{
    public required string EventId { get; init; }

    public required DateTimeOffset SnapshotAt { get; init; }

    /// <summary>adsb.lol endpoint that produced the observation.</summary>
    public required string Source { get; init; }

    /// <summary>Configured watch zone, or "global" for emergency/military queries.</summary>
    public required string Zone { get; init; }

    /// <summary>ICAO 24-bit address. Used as the ordering key.</summary>
    public required string Hex { get; init; }

    public string? Callsign { get; init; }

    public string? Registration { get; init; }

    public string? AircraftType { get; init; }

    public string? Category { get; init; }

    public double? Latitude { get; init; }

    public double? Longitude { get; init; }

    public int? AltitudeBaroFt { get; init; }

    public int? AltitudeGeomFt { get; init; }

    public double? GroundSpeedKt { get; init; }

    public double? TrackDeg { get; init; }

    public double? VerticalRateFpm { get; init; }

    public string? Squawk { get; init; }

    public string? Emergency { get; init; }

    public bool OnGround { get; init; }

    /// <summary>Seconds since the last position update reported by adsb.lol.</summary>
    public double? SeenPositionSeconds { get; init; }

    public bool Military { get; init; }

    public bool PrivacyIcao { get; init; }

    public bool Ladd { get; init; }

    [JsonIgnore]
    public bool IsEmergency =>
        Squawk is "7500" or "7600" or "7700" ||
        (!string.IsNullOrWhiteSpace(Emergency) &&
         !string.Equals(Emergency, "none", StringComparison.OrdinalIgnoreCase));

    [JsonIgnore]
    public bool HasPosition => Latitude.HasValue && Longitude.HasValue;
}
