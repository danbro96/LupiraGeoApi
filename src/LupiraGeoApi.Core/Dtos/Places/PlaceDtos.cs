using LupiraGeoApi.Domain;
using LupiraGeoApi.Dtos.AdminAreas;
using System.Text.Json.Serialization;

namespace LupiraGeoApi.Dtos.Places;

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
    public List<string> Aliases { get; set; } = [];
    public List<AdminAreaDto> Containment { get; set; } = [];
    public List<PlaceExternalIdDto> ExternalIds { get; set; } = [];
}

public sealed class PlaceExternalIdDto
{
    [JsonConverter(typeof(JsonStringEnumConverter<ExternalScheme>))]
    public required ExternalScheme Scheme { get; set; }
    public required string Value { get; set; }
}

/// <summary>Create a user place directly (name + optional coordinates/category). The missing write path today.</summary>
public sealed class CreatePlaceRequest
{
    public required string Name { get; set; }
    public PlaceKind Kind { get; set; } = PlaceKind.Poi;
    public PlaceCategory Category { get; set; } = PlaceCategory.Unknown;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? FormattedAddress { get; set; }
    public Guid? WithinAreaId { get; set; }
}

/// <summary>Curate a place: rename, recategorize, or verify. Omitted members are left unchanged.</summary>
public sealed class UpdatePlaceRequest
{
    public string? Name { get; set; }
    public PlaceCategory? Category { get; set; }
    public bool? Verified { get; set; }
}

/// <summary>Resolve free-text to a place id — match an existing entry, geocode, or provisionally create. This is what
/// LupiraCalApi calls when an item/travel-leg/contact address carries a location string.</summary>
public sealed class ResolvePlaceRequest
{
    public required string Text { get; set; }
}

public sealed class ResolvePlaceResponse
{
    public required Guid PlaceId { get; set; }
    public required string Name { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}
