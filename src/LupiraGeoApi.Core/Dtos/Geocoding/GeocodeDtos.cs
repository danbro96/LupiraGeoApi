using LupiraGeoApi.Domain;
using System.Text.Json.Serialization;

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
}
