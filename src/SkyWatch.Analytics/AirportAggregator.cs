using SkyWatch.Contracts;

namespace SkyWatch.Analytics;

public sealed record AirportAggregationResult(
    IReadOnlyList<AirportCongestion> Airports,
    FleetStats Fleet);

/// <summary>
/// Keeps the latest position per aircraft and produces airport congestion plus
/// global fleet snapshots. It needs all positions, so it runs as a single consumer.
/// </summary>
public sealed class AirportAggregator
{
    private readonly AirportOptions _options;
    private readonly Dictionary<string, AircraftPosition> _latest = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AircraftTrack> _tracks = new(StringComparer.OrdinalIgnoreCase);

    public AirportAggregator(AirportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public void Process(AircraftPosition position)
    {
        ArgumentNullException.ThrowIfNull(position);

        if (!position.HasPosition)
        {
            return;
        }

        _latest[position.Hex] = position;

        if (!_tracks.TryGetValue(position.Hex, out AircraftTrack? track))
        {
            track = new AircraftTrack();
            _tracks[position.Hex] = track;
        }

        track.Add(position.SnapshotAt, position.TrackDeg, TimeSpan.FromSeconds(_options.HoldingWindowSeconds));
    }

    public AirportAggregationResult Snapshot(DateTimeOffset now)
    {
        RemoveStale(now);

        var airports = new List<AirportCongestion>(_options.Airports.Count);

        foreach (AirportConfig airport in _options.Airports)
        {
            double radius = Math.Max(1, airport.RadiusNm);

            AircraftPosition[] inRange = _latest.Values
                .Where(position => position.HasPosition &&
                                   GeoMath.DistanceNm(
                                       airport.Latitude,
                                       airport.Longitude,
                                       position.Latitude!.Value,
                                       position.Longitude!.Value) <= radius)
                .ToArray();

            AircraftPosition[] airborne = inRange.Where(position => !position.OnGround).ToArray();

            int approach = airborne.Count(IsApproaching);
            int departures = airborne.Count(IsDeparting);
            int holdings = airborne.Count(position => IsHolding(position, now));

            airports.Add(new AirportCongestion
            {
                AirportCode = airport.Code,
                AirportName = airport.Name,
                Latitude = airport.Latitude,
                Longitude = airport.Longitude,
                WindowEnd = now,
                AircraftInRange = inRange.Length,
                OnGround = inRange.Count(position => position.OnGround),
                ApproachQueue = approach,
                Departures = departures,
                Holdings = holdings,
                AverageAltitudeFt = Average(airborne, position => position.AltitudeBaroFt),
                AverageSpeedKt = Average(airborne, position => position.GroundSpeedKt)
            });
        }

        return new AirportAggregationResult(airports, BuildFleetStats(now));
    }

    private FleetStats BuildFleetStats(DateTimeOffset now)
    {
        AircraftPosition[] latest = _latest.Values.ToArray();

        return new FleetStats
        {
            WindowEnd = now,
            Aircraft = latest.Length,
            Military = latest.Count(position => position.Military),
            OnGround = latest.Count(position => position.OnGround),
            Emergency = latest.Count(position => position.IsEmergency),
            ByAirline = CountBy(latest, position => AirlineCatalog.ForCallsign(position.Callsign)),
            ByType = CountBy(latest, position => position.AircraftType ?? "unknown"),
            ByAltitudeBand = CountBy(latest, AltitudeBand.For)
        };
    }

    private bool IsHolding(AircraftPosition position, DateTimeOffset now)
    {
        if (position.AltitudeBaroFt is not { } altitude ||
            position.GroundSpeedKt is not { } speed ||
            altitude < _options.HoldingMinAltitudeFt ||
            altitude > _options.HoldingMaxAltitudeFt ||
            speed < _options.HoldingMinSpeedKt ||
            speed > _options.HoldingMaxSpeedKt)
        {
            return false;
        }

        return _tracks.TryGetValue(position.Hex, out AircraftTrack? track) &&
               track.CumulativeTurn(now, TimeSpan.FromSeconds(_options.HoldingWindowSeconds)) >= _options.HoldingTurnDegrees;
    }

    private static bool IsApproaching(AircraftPosition position) =>
        position.AltitudeBaroFt is > 0 and < 10_000 &&
        position.GroundSpeedKt is > 0 and < 300 &&
        position.VerticalRateFpm is < -200;

    private static bool IsDeparting(AircraftPosition position) =>
        position.AltitudeBaroFt is > 0 and < 10_000 &&
        position.VerticalRateFpm is > 200;

    private void RemoveStale(DateTimeOffset now)
    {
        TimeSpan timeout = TimeSpan.FromMinutes(Math.Max(1, _options.AircraftTimeoutMinutes));
        var stale = _latest
            .Where(pair => now - pair.Value.SnapshotAt >= timeout)
            .Select(pair => pair.Key)
            .ToArray();

        foreach (string hex in stale)
        {
            _latest.Remove(hex);
            _tracks.Remove(hex);
        }
    }

    private static IReadOnlyDictionary<string, int> CountBy(
        IEnumerable<AircraftPosition> positions,
        Func<AircraftPosition, string> selector) =>
        positions
            .GroupBy(selector, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

    private static double Average(
        IEnumerable<AircraftPosition> positions,
        Func<AircraftPosition, double?> selector)
    {
        double[] values = positions
            .Select(selector)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();

        return values.Length == 0 ? 0 : values.Average();
    }

    private sealed class AircraftTrack
    {
        private readonly Queue<TrackPoint> _points = new();

        public void Add(DateTimeOffset at, double? track, TimeSpan window)
        {
            if (track is null)
            {
                return;
            }

            _points.Enqueue(new TrackPoint(at, track.Value));

            while (_points.Count > 0 && at - _points.Peek().At > window)
            {
                _points.Dequeue();
            }
        }

        public double CumulativeTurn(DateTimeOffset now, TimeSpan window)
        {
            TrackPoint[] points = _points
                .Where(point => now - point.At <= window)
                .ToArray();

            double total = 0;

            for (int index = 1; index < points.Length; index++)
            {
                total += Math.Abs(GeoMath.NormalizeTurn(points[index].Track - points[index - 1].Track));
            }

            return total;
        }

        private sealed record TrackPoint(DateTimeOffset At, double Track);
    }
}
