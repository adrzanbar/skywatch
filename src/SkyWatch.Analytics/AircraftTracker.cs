using SkyWatch.Contracts;

namespace SkyWatch.Analytics;

/// <summary>
/// Stateful per-aircraft alert engine. It is keyed by hex, so a consumer group
/// can keep one aircraft's state on one partition/consumer.
/// </summary>
public sealed class AircraftTracker
{
    private readonly TrackerOptions _options;
    private readonly Dictionary<string, TrackedAircraft> _aircraft = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _lastCleanup = DateTimeOffset.MinValue;

    public AircraftTracker(TrackerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public IReadOnlyList<FlightAlert> Process(AircraftPosition position)
    {
        ArgumentNullException.ThrowIfNull(position);

        DateTimeOffset now = position.SnapshotAt;
        var changes = new List<FlightAlert>();

        if (now - _lastCleanup >= TimeSpan.FromSeconds(30))
        {
            changes.AddRange(CloseStaleAircraft(now));
            _lastCleanup = now;
        }

        if (!_aircraft.TryGetValue(position.Hex, out TrackedAircraft? state))
        {
            state = new TrackedAircraft();
            _aircraft[position.Hex] = state;
        }

        EvaluateEmergency(position, state, now, changes);
        EvaluateRapidDescent(position, state, now, changes);
        EvaluateSignalLoss(position, state, now, changes);
        EvaluateSquawkChange(position, state, now, changes);
        EvaluateWatchlist(position, state, now, changes);

        state.Last = position;
        state.LastSeenAt = now;
        state.LastSquawk = position.Squawk;

        return changes;
    }

    private void EvaluateEmergency(
        AircraftPosition position,
        TrackedAircraft state,
        DateTimeOffset now,
        List<FlightAlert> changes)
    {
        if (position.IsEmergency)
        {
            if (!state.ActiveAlerts.ContainsKey(AlertKind.Emergency))
            {
                string detail = position.Squawk is "7500" or "7600" or "7700"
                    ? $"squawking {position.Squawk}"
                    : $"emergency '{position.Emergency}'";
                changes.Add(OpenAlert(position, state, AlertKind.Emergency, AlertSeverity.Critical, $"{Label(position)} {detail}.", now));
            }

            return;
        }

        CloseAlert(position, state, AlertKind.Emergency, now, changes);
    }

    private void EvaluateRapidDescent(
        AircraftPosition position,
        TrackedAircraft state,
        DateTimeOffset now,
        List<FlightAlert> changes)
    {
        bool descending = position.VerticalRateFpm.HasValue &&
                          position.VerticalRateFpm.Value <= _options.RapidDescentFpm;

        if (descending)
        {
            if (!state.ActiveAlerts.ContainsKey(AlertKind.RapidDescent))
            {
                changes.Add(OpenAlert(
                    position,
                    state,
                    AlertKind.RapidDescent,
                    AlertSeverity.Warning,
                    $"{Label(position)} descending at {position.VerticalRateFpm:0} ft/min.",
                    now));
            }

            return;
        }

        bool recovered = position.OnGround ||
                         (position.VerticalRateFpm.HasValue &&
                          position.VerticalRateFpm.Value >= _options.RapidDescentRecoveryFpm);

        if (recovered)
        {
            CloseAlert(position, state, AlertKind.RapidDescent, now, changes);
        }
    }

    private void EvaluateSignalLoss(
        AircraftPosition position,
        TrackedAircraft state,
        DateTimeOffset now,
        List<FlightAlert> changes)
    {
        bool lost = !position.OnGround &&
                    position.SeenPositionSeconds.HasValue &&
                    position.SeenPositionSeconds.Value >= _options.SignalLossSeconds;

        if (lost)
        {
            if (!state.ActiveAlerts.ContainsKey(AlertKind.SignalLoss))
            {
                changes.Add(OpenAlert(
                    position,
                    state,
                    AlertKind.SignalLoss,
                    AlertSeverity.Warning,
                    $"{Label(position)} has not reported a position for {position.SeenPositionSeconds:0} s.",
                    now));
            }

            return;
        }

        if (position.OnGround ||
            !position.SeenPositionSeconds.HasValue ||
            position.SeenPositionSeconds.Value < _options.SignalLossSeconds)
        {
            CloseAlert(position, state, AlertKind.SignalLoss, now, changes);
        }
    }

    private void EvaluateSquawkChange(
        AircraftPosition position,
        TrackedAircraft state,
        DateTimeOffset now,
        List<FlightAlert> changes)
    {
        if (state.LastSquawk is null ||
            position.Squawk is null ||
            string.Equals(state.LastSquawk, position.Squawk, StringComparison.Ordinal))
        {
            return;
        }

        if (position.IsEmergency || IsEmergencySquawk(state.LastSquawk))
        {
            return;
        }

        FlightAlert transient = CreateAlert(
            position,
            AlertKind.SquawkChange,
            AlertSeverity.Info,
            $"{Label(position)} changed squawk from {state.LastSquawk} to {position.Squawk}.",
            now);

        changes.Add(transient);
        changes.Add(transient with
        {
            Status = AlertStatus.Closed,
            ClosedAt = now
        });
    }

    private void EvaluateWatchlist(
        AircraftPosition position,
        TrackedAircraft state,
        DateTimeOffset now,
        List<FlightAlert> changes)
    {
        bool matches = _options.Watchlist.Any(entry => Matches(entry, position));

        if (matches)
        {
            if (!state.ActiveAlerts.ContainsKey(AlertKind.Watchlist))
            {
                changes.Add(OpenAlert(
                    position,
                    state,
                    AlertKind.Watchlist,
                    AlertSeverity.Info,
                    $"{Label(position)} matched the watchlist.",
                    now));
            }

            return;
        }

        CloseAlert(position, state, AlertKind.Watchlist, now, changes);
    }

    private IEnumerable<FlightAlert> CloseStaleAircraft(DateTimeOffset now)
    {
        var stale = new List<FlightAlert>();
        TimeSpan timeout = TimeSpan.FromMinutes(Math.Max(1, _options.AircraftTimeoutMinutes));

        foreach ((string hex, TrackedAircraft state) in _aircraft.ToArray())
        {
            if (now - state.LastSeenAt < timeout)
            {
                continue;
            }

            foreach (AlertKind kind in state.ActiveAlerts.Keys.ToArray())
            {
                FlightAlert? closed = CloseAlert(state.Last, state, kind, now, stale);
                _ = closed;
            }

            _aircraft.Remove(hex);
        }

        return stale;
    }

    private FlightAlert OpenAlert(
        AircraftPosition position,
        TrackedAircraft state,
        AlertKind kind,
        AlertSeverity severity,
        string message,
        DateTimeOffset now)
    {
        FlightAlert alert = CreateAlert(position, kind, severity, message, now);
        state.ActiveAlerts[kind] = new ActiveAlert(alert);
        return alert;
    }

    private FlightAlert? CloseAlert(
        AircraftPosition position,
        TrackedAircraft state,
        AlertKind kind,
        DateTimeOffset now,
        List<FlightAlert> changes)
    {
        if (!state.ActiveAlerts.Remove(kind, out ActiveAlert? active))
        {
            return null;
        }

        FlightAlert closed = active.Alert with
        {
            Status = AlertStatus.Closed,
            ClosedAt = now,
            Latitude = position.Latitude ?? active.Alert.Latitude,
            Longitude = position.Longitude ?? active.Alert.Longitude,
            AltitudeFt = position.AltitudeBaroFt ?? active.Alert.AltitudeFt,
            Squawk = position.Squawk ?? active.Alert.Squawk
        };

        changes.Add(closed);
        return closed;
    }

    private static FlightAlert CreateAlert(
        AircraftPosition position,
        AlertKind kind,
        AlertSeverity severity,
        string message,
        DateTimeOffset now) =>
        new()
        {
            AlertId = $"{position.Hex}:{kind}:{now.ToUnixTimeMilliseconds()}",
            Hex = position.Hex,
            Callsign = position.Callsign,
            Registration = position.Registration,
            Kind = kind,
            Severity = severity,
            Status = AlertStatus.Open,
            OpenedAt = now,
            Message = message,
            Latitude = position.Latitude,
            Longitude = position.Longitude,
            AltitudeFt = position.AltitudeBaroFt,
            Squawk = position.Squawk
        };

    private static bool Matches(string entry, AircraftPosition position)
    {
        if (string.IsNullOrWhiteSpace(entry))
        {
            return false;
        }

        string value = entry.Trim();
        return string.Equals(value, position.Hex, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, position.Callsign, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, position.Registration, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsEmergencySquawk(string squawk) => squawk is "7500" or "7600" or "7700";

    private static string Label(AircraftPosition position) =>
        position.Callsign ?? position.Registration ?? position.Hex;

    private sealed class TrackedAircraft
    {
        public AircraftPosition Last { get; set; } = null!;

        public DateTimeOffset LastSeenAt { get; set; }

        public string? LastSquawk { get; set; }

        public Dictionary<AlertKind, ActiveAlert> ActiveAlerts { get; } = [];
    }

    private sealed record ActiveAlert(FlightAlert Alert);
}
