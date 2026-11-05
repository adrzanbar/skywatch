namespace SkyWatch.Analytics;

public sealed class AirportOptions
{
    public List<AirportConfig> Airports { get; set; } =
    [
        new() { Code = "EZE", Name = "Buenos Aires Ezeiza", Latitude = -34.8222, Longitude = -58.5358, RadiusNm = 35 },
        new() { Code = "AEP", Name = "Buenos Aires Aeroparque", Latitude = -34.5592, Longitude = -58.4156, RadiusNm = 25 },
        new() { Code = "MAD", Name = "Madrid Barajas", Latitude = 40.4719, Longitude = -3.5626, RadiusNm = 35 },
        new() { Code = "JFK", Name = "New York JFK", Latitude = 40.6413, Longitude = -73.7781, RadiusNm = 35 },
        new() { Code = "LHR", Name = "London Heathrow", Latitude = 51.47, Longitude = -0.4543, RadiusNm = 35 },
        new() { Code = "HND", Name = "Tokyo Haneda", Latitude = 35.5494, Longitude = 139.7798, RadiusNm = 35 },
        new() { Code = "MIA", Name = "Miami International", Latitude = 25.7959, Longitude = -80.287, RadiusNm = 35 },
        new() { Code = "GRU", Name = "Sao Paulo Guarulhos", Latitude = -23.4356, Longitude = -46.4731, RadiusNm = 35 }
    ];

    public double HoldingMinAltitudeFt { get; set; } = 3_000;

    public double HoldingMaxAltitudeFt { get; set; } = 15_000;

    public double HoldingMinSpeedKt { get; set; } = 150;

    public double HoldingMaxSpeedKt { get; set; } = 300;

    public int HoldingWindowSeconds { get; set; } = 120;

    public double HoldingTurnDegrees { get; set; } = 270;

    public int AircraftTimeoutMinutes { get; set; } = 10;
}

public sealed class AirportConfig
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    public double RadiusNm { get; set; } = 30;
}
