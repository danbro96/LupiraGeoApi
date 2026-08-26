using LupiraGeoApi.Domain;

namespace LupiraGeoApi.Application;

/// <summary>A geocoding hit: a coordinate + display label + best-effort structured address and category.</summary>
public sealed record GeocodeHit(
    string DisplayName, double Lat, double Lon, PlaceCategory Category,
    string? CountryCode, string? Country, string? Region, string? Locality,
    string? OsmType, long? OsmId);

/// <summary>Outcome of a forward geocode. <c>Ok</c> carries hits; <c>Empty</c> is a definitive "no such place"
/// (safe to freeze/provision); <c>Unavailable</c> means no endpoint could be reached (transport error/timeout/429/5xx
/// after retries) — transient, NOT a no-hit, so callers must not persist a coordinate-less stub for it.</summary>
public enum GeocodeStatus { Ok, Empty, Unavailable }

public sealed record ForwardResult(GeocodeStatus Status, IReadOnlyList<GeocodeHit> Hits)
{
    public static readonly ForwardResult Empty = new(GeocodeStatus.Empty, []);
    public static readonly ForwardResult Unavailable = new(GeocodeStatus.Unavailable, []);
    public static ForwardResult FromHits(IReadOnlyList<GeocodeHit> hits) => new(GeocodeStatus.Ok, hits);
}
