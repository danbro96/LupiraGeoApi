using LupiraGeoApi.Core.Domain;

namespace LupiraGeoApi.Core.Dtos.Places;

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
