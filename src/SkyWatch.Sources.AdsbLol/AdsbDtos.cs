using System.Text.Json;
using System.Text.Json.Serialization;

namespace SkyWatch.Sources.AdsbLol;

internal sealed class AdsbResponse
{
    [JsonPropertyName("ac")]
    public List<AdsbAircraft> Aircraft { get; set; } = [];

    [JsonPropertyName("now")]
    public long Now { get; set; }

    [JsonPropertyName("total")]
    public int Total { get; set; }
}

internal sealed class AdsbAircraft
{
    [JsonPropertyName("hex")]
    public string? Hex { get; set; }

    [JsonPropertyName("flight")]
    public string? Flight { get; set; }

    [JsonPropertyName("r")]
    public string? Registration { get; set; }

    [JsonPropertyName("t")]
    public string? Type { get; set; }

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("alt_baro")]
    public JsonElement? AltBaro { get; set; }

    [JsonPropertyName("alt_geom")]
    public JsonElement? AltGeom { get; set; }

    [JsonPropertyName("gs")]
    public double? GroundSpeed { get; set; }

    [JsonPropertyName("track")]
    public double? Track { get; set; }

    [JsonPropertyName("baro_rate")]
    public double? BaroRate { get; set; }

    [JsonPropertyName("geom_rate")]
    public double? GeomRate { get; set; }

    [JsonPropertyName("squawk")]
    public string? Squawk { get; set; }

    [JsonPropertyName("emergency")]
    public string? Emergency { get; set; }

    [JsonPropertyName("lat")]
    public double? Latitude { get; set; }

    [JsonPropertyName("lon")]
    public double? Longitude { get; set; }

    [JsonPropertyName("seen_pos")]
    public double? SeenPosition { get; set; }

    [JsonPropertyName("dbFlags")]
    public int? DbFlags { get; set; }
}
