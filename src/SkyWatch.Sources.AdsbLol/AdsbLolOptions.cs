namespace SkyWatch.Sources.AdsbLol;

public sealed class AdsbLolOptions
{
    public string BaseUrl { get; set; } = "https://api.adsb.lol";

    public string UserAgent { get; set; } = "skywatch/0.1 (distributed-systems-lab)";

    public int PollSeconds { get; set; } = 10;

    /// <summary>Delay between adsb.lol requests. The public API rate-limits bursts.</summary>
    public int RequestSpacingMs { get; set; } = 500;

    /// <summary>Cap for the server's Retry-After header so one 429 does not stall a poll.</summary>
    public int MaxRetryAfterSeconds { get; set; } = 5;

    /// <summary>
    /// Maximum aircraft kept per snapshot. Zero means no limit. K0sStreams publishes
    /// one HTTP request per record, so a cap keeps a poll bounded.
    /// </summary>
    public int MaxPositionsPerSnapshot { get; set; } = 200;

    public bool IncludeMilitary { get; set; } = true;

    public bool IncludeEmergencySquawks { get; set; } = true;

    public string[] EmergencySquawks { get; set; } = ["7500", "7600", "7700"];

    public List<WatchZone> Zones { get; set; } =
    [
        new() { Name = "buenos-aires", Latitude = -34.6, Longitude = -58.4, RadiusNm = 250 },
        new() { Name = "madrid", Latitude = 40.47, Longitude = -3.56, RadiusNm = 250 },
        new() { Name = "new-york", Latitude = 40.64, Longitude = -73.78, RadiusNm = 250 },
        new() { Name = "london", Latitude = 51.47, Longitude = -0.45, RadiusNm = 250 },
        new() { Name = "tokyo", Latitude = 35.55, Longitude = 139.78, RadiusNm = 250 }
    ];
}

public sealed class WatchZone
{
    public string Name { get; set; } = string.Empty;

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    public int RadiusNm { get; set; } = 250;
}
