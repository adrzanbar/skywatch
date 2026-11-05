using SkyWatch.Contracts;

namespace SkyWatch.Sources.AdsbLol;

/// <summary>A source of aircraft snapshots. The producer does not know adsb.lol's HTTP shape.</summary>
public interface IAircraftSource
{
    IAsyncEnumerable<AircraftSnapshot> ReadAsync(CancellationToken cancellationToken);
}

public sealed record AircraftSnapshot(
    DateTimeOffset SnapshotAt,
    IReadOnlyList<AircraftPosition> Positions);
