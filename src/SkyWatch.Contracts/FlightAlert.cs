namespace SkyWatch.Contracts;

public enum AlertKind
{
    Emergency,
    RapidDescent,
    SignalLoss,
    SquawkChange,
    Holding,
    Watchlist
}

public enum AlertSeverity
{
    Info,
    Warning,
    Critical
}

public enum AlertStatus
{
    Open,
    Closed
}

/// <summary>An alert opened or closed by the tracker. Closing reuses the same AlertId.</summary>
public sealed record FlightAlert
{
    public required string AlertId { get; init; }

    public required string Hex { get; init; }

    public string? Callsign { get; init; }

    public string? Registration { get; init; }

    public required AlertKind Kind { get; init; }

    public required AlertSeverity Severity { get; init; }

    public required AlertStatus Status { get; init; }

    public required DateTimeOffset OpenedAt { get; init; }

    public DateTimeOffset? ClosedAt { get; init; }

    public required string Message { get; init; }

    public double? Latitude { get; init; }

    public double? Longitude { get; init; }

    public int? AltitudeFt { get; init; }

    public string? Squawk { get; init; }
}
