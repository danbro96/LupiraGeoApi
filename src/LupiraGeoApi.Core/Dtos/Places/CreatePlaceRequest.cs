using LupiraGeoApi.Core.Domain;

namespace LupiraGeoApi.Core.Dtos.Places;

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
