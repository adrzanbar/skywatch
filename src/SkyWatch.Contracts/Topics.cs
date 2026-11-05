namespace SkyWatch.Contracts;

/// <summary>Logical topic names shared by every producer and consumer.</summary>
public static class Topics
{
    public const string Positions = "flight-positions";
    public const string Alerts = "flight-alerts";
    public const string Stats = "flight-stats";
    public const string DeadLetter = "flight-dlq";
}
