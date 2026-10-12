using LupiraGeoApi.Core.Domain;

namespace LupiraGeoApi.Core.Application.Geocoding;

/// <summary>A geocoding hit: a coordinate + display label + best-effort structured address and category.
/// <c>IsArea</c> marks a settlement/administrative-area hit (see <see cref="AreaClassifier"/>); <c>IsCoarse</c> marks
/// any hit coarser than a building or venue (a street, locality or area).</summary>
public sealed record GeocodeHit(
    string DisplayName, double Lat, double Lon, PlaceCategory Category,
    string? CountryCode, string? Country, string? Region, string? Locality,
    string? OsmType, long? OsmId, bool IsArea = false, string? Postcode = null, double? Importance = null,
    string? HouseNumber = null, bool IsCoarse = false);
