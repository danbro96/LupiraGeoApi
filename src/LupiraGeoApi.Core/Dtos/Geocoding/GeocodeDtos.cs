using System.Text.Json.Serialization;
using LupiraGeoApi.Domain;

namespace LupiraGeoApi.Dtos.Geocoding;

/// <summary>A geocoding hit — a coordinate + display label + best-effort structured address and category. Coarse by
/// design (coordinates are quantized to the cache grid).</summary>
public sealed class GeocodeResultDto
{
    public required string DisplayName { get; set; }
    public required double Latitude { get; set; }
    public required double Longitude { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter<PlaceCategory>))]
    public required PlaceCategory Category { get; set; }
    public string? CountryCode { get; set; }
    public string? Country { get; set; }
    public string? Region { get; set; }
    public string? Locality { get; set; }

    /// <summary>OSM identity of the hit (e.g. "way" + 175761024) — the key for <c>POST /places/from-geocode</c>.</summary>
    public string? OsmType { get; set; }
    public long? OsmId { get; set; }
}
