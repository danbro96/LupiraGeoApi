namespace LupiraGeoApi.Core.Domain;

/// <summary>How <see cref="Place"/> resolution landed: an existing entry matched, a fresh geocode created one, a
/// coordinate-less provisional stub was created (address not found), the geocoder was unreachable so nothing was
/// created — retryable and must NOT be mistaken for "not found" — or a dropped pin created one.</summary>
public enum PlaceResolution
{
    Matched,
    Geocoded,
    Provisional,
    GeocodeUnavailable,
    Created,
}
