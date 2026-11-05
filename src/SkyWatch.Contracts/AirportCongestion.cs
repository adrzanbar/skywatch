namespace SkyWatch.Contracts;

/// <summary>Snapshot of traffic around one configured airport.</summary>
public sealed record AirportCongestion
{
    public required string AirportCode { get; init; }

    public required string AirportName { get; init; }

    public required double Latitude { get; init; }

    public required double Longitude { get; init; }

    public required DateTimeOffset WindowEnd { get; init; }

    public required int AircraftInRange { get; init; }

    public required int OnGround { get; init; }

    public required int ApproachQueue { get; init; }

    public required int Departures { get; init; }

    public required int Holdings { get; init; }

    public required double AverageAltitudeFt { get; init; }

    public required double AverageSpeedKt { get; init; }
}
