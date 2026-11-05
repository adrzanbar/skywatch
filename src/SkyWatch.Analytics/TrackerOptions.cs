namespace SkyWatch.Analytics;

public sealed class TrackerOptions
{
    public double RapidDescentFpm { get; set; } = -3_000;

    public double RapidDescentRecoveryFpm { get; set; } = -1_000;

    public double SignalLossSeconds { get; set; } = 60;

    public int AircraftTimeoutMinutes { get; set; } = 10;

    public List<string> Watchlist { get; set; } = [];
}
