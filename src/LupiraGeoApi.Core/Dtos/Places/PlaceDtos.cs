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
    public List<PlaceAliasDto> Aliases { get; set; } = [];
    public List<AdminAreaDto> Containment { get; set; } = [];
    public List<PlaceExternalIdDto> ExternalIds { get; set; } = [];
}

public sealed class PlaceAliasDto
{
    public required Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Lang { get; set; }
}

public sealed class PlaceExternalIdDto
{
    [JsonConverter(typeof(JsonStringEnumConverter<ExternalScheme>))]
    public required ExternalScheme Scheme { get; set; }
    public required string Value { get; set; }
}

/// <summary>A typeahead suggestion: a gazetteer place (name/alias trigram match) or an AdminArea locality — cities come
/// from the GeoNames seed, so they suggest without anyone having geocoded a POI there. <c>Context</c> disambiguates
/// (formatted address for places, parent area for localities).</summary>
public sealed class PlaceSuggestionDto
{
    public required Guid Id { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter<SuggestionType>))]
    public required SuggestionType Type { get; set; }

    public required string Name { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter<PlaceCategory>))]
    public PlaceCategory? Category { get; set; }

    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? Context { get; set; }
}

/// <summary>Create a user place directly (name + optional coordinates/category).</summary>
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

/// <summary>Curate a place: rename, recategorize, verify, or correct its location. Omitted members are left unchanged.
/// <c>Latitude</c>+<c>Longitude</c> (both required together) move the point — for fixing a wrong geocode by hand;
/// <c>WithinAreaId</c> re-anchors containment to match.</summary>
public sealed class UpdatePlaceRequest
{
    public string? Name { get; set; }
    public PlaceCategory? Category { get; set; }
    public bool? Verified { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? FormattedAddress { get; set; }
    public Guid? WithinAreaId { get; set; }
}

public sealed class AddAliasRequest
{
    public required string Name { get; set; }
    public string? Lang { get; set; }
}

/// <summary>Merge the addressed place into <see cref="IntoPlaceId"/> (the survivor). The addressed id becomes a
/// tombstone redirect, so ids held by other services keep resolving.</summary>
public sealed class MergePlaceRequest
{
    public required Guid IntoPlaceId { get; set; }
}

/// <summary>Bulk <see cref="ResolvePlaceRequest"/> — for imports. Responses align index-for-index with the input.</summary>
public sealed class ResolvePlacesBatchRequest
{
    public required List<string> Texts { get; set; }
}

/// <summary>Resolve free-text to a place id — match an existing entry, geocode, or provisionally create. This is what
/// LupiraCalApi calls when an item/travel-leg/contact address carries a location string.</summary>
public sealed class ResolvePlaceRequest
{
    public required string Text { get; set; }
}

public sealed class ResolvePlaceResponse
{
    [JsonConverter(typeof(JsonStringEnumConverter<PlaceResolution>))]
    public required PlaceResolution Resolution { get; set; }

    /// <summary>Null only when <see cref="Resolution"/> is <see cref="PlaceResolution.GeocodeUnavailable"/> — the
    /// geocoder was unreachable and nothing was created; the item is retryable.</summary>
    public Guid? PlaceId { get; set; }
    public required string Name { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}
