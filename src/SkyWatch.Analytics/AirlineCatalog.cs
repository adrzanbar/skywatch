namespace SkyWatch.Analytics;

internal static class AirlineCatalog
{
    private static readonly Dictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ARG"] = "Aerolineas Argentinas",
        ["LAN"] = "LATAM",
        ["LPE"] = "LATAM Peru",
        ["GLO"] = "Gol",
        ["AZU"] = "Azul",
        ["TAM"] = "LATAM Brasil",
        ["DAL"] = "Delta",
        ["UAL"] = "United",
        ["AAL"] = "American",
        ["SWA"] = "Southwest",
        ["BAW"] = "British Airways",
        ["IBE"] = "Iberia",
        ["AEA"] = "Air Europa",
        ["VLG"] = "Vueling",
        ["RYR"] = "Ryanair",
        ["EZY"] = "easyJet",
        ["AFR"] = "Air France",
        ["DLH"] = "Lufthansa",
        ["SWR"] = "Swiss",
        ["KLM"] = "KLM",
        ["JAL"] = "Japan Airlines",
        ["ANA"] = "All Nippon Airways",
        ["CPA"] = "Cathay Pacific",
        ["QFA"] = "Qantas",
        ["UAE"] = "Emirates",
        ["ETD"] = "Etihad",
        ["QTR"] = "Qatar Airways",
        ["THY"] = "Turkish Airlines",
        ["AMX"] = "Aeromexico",
        ["AVA"] = "Avianca",
        ["CMP"] = "Copa",
        ["SKU"] = "Sky Airline",
        ["JBU"] = "JetBlue",
        ["FDX"] = "FedEx",
        ["UPS"] = "UPS"
    };

    public static string ForCallsign(string? callsign)
    {
        if (string.IsNullOrWhiteSpace(callsign) || callsign.Length < 3)
        {
            return "Unknown";
        }

        string prefix = callsign.Trim()[..3];
        return Known.TryGetValue(prefix, out string? airline) ? airline : "Other";
    }
}

internal static class AltitudeBand
{
    public static string For(SkyWatch.Contracts.AircraftPosition position)
    {
        if (position.OnGround)
        {
            return "ground";
        }

        if (position.AltitudeBaroFt is not { } altitude)
        {
            return "unknown";
        }

        return altitude switch
        {
            < 10_000 => "0-10k",
            < 20_000 => "10-20k",
            < 30_000 => "20-30k",
            < 40_000 => "30-40k",
            _ => "40k+"
        };
    }
}
