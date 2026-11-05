namespace SkyWatch.Contracts;

/// <summary>Global fleet snapshot computed from the latest position per aircraft.</summary>
public sealed record FleetStats
{
    public required DateTimeOffset WindowEnd { get; init; }

    public required int Aircraft { get; init; }

    public required int Military { get; init; }

    public required int OnGround { get; init; }

    public required int Emergency { get; init; }

    public required IReadOnlyDictionary<string, int> ByAirline { get; init; }

    public required IReadOnlyDictionary<string, int> ByType { get; init; }

    public required IReadOnlyDictionary<string, int> ByAltitudeBand { get; init; }
}
