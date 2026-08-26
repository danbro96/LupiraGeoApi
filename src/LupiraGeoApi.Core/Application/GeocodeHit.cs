using LupiraGeoApi.Core.Domain;

namespace LupiraGeoApi.Core.Application;

/// <summary>A geocoding hit: a coordinate + display label + best-effort structured address and category.</summary>
public sealed record GeocodeHit(
    string DisplayName, double Lat, double Lon, PlaceCategory Category,
    string? CountryCode, string? Country, string? Region, string? Locality,
    string? OsmType, long? OsmId);
