using LupiraGeoApi.Core.Domain;

namespace LupiraGeoApi.Core.Application.Geocoding;

/// <summary>A geocoding hit: a coordinate + display label + best-effort structured address and category.
/// <c>IsArea</c> marks a settlement/administrative-area hit (see <see cref="AreaClassifier"/>).</summary>
public sealed record GeocodeHit(
    string DisplayName, double Lat, double Lon, PlaceCategory Category,
    string? CountryCode, string? Country, string? Region, string? Locality,
    string? OsmType, long? OsmId, bool IsArea = false, string? Postcode = null, double? Importance = null);
