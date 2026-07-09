using LupiraGeoApi.Domain;
using System.Text.Json.Serialization;

namespace LupiraGeoApi.Dtos.AdminAreas;

/// <summary>A node in the administrative containment tree (Country/Region/Locality).</summary>
public sealed class AdminAreaDto
{
    public required Guid Id { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter<AdminLevel>))]
    public required AdminLevel Level { get; set; }
    public required string Name { get; set; }
    public string? IsoCode { get; set; }
    public Guid? WithinAreaId { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}
