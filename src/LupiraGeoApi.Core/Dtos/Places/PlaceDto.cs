using System.Text.Json.Serialization;
using LupiraGeoApi.Core.Domain;
using LupiraGeoApi.Core.Dtos.AdminAreas;

namespace LupiraGeoApi.Core.Dtos.Places;

/// <summary>A gazetteer place. Coordinates are plain lat/lon on the wire; <c>Containment</c> is the AdminArea chain
/// outermost→innermost. <c>DistanceM</c> is populated only on proximity (<c>near=</c>) searches.</summary>
public sealed class PlaceDto
{
    public required Guid Id { get; set; }
    public required string Name { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter<PlaceKind>))]
    public required PlaceKind Kind { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter<PlaceCategory>))]
    public required PlaceCategory Category { get; set; }

    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? FormattedAddress { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter<PlaceSource>))]
    public required PlaceSource Source { get; set; }

    public required bool Verified { get; set; }
    public Guid? WithinAreaId { get; set; }
    public double? DistanceM { get; set; }
    public List<PlaceAliasDto> Aliases { get; set; } = [];
    public List<AdminAreaDto> Containment { get; set; } = [];
    public List<PlaceExternalIdDto> ExternalIds { get; set; } = [];
}
