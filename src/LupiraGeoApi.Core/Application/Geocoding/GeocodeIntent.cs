namespace LupiraGeoApi.Core.Application.Geocoding;

/// <summary><c>Search</c> resolves a finished query against the canonical geocoder; <c>Typeahead</c> suggests while the
/// user types and never reaches a rate-limited public endpoint or the frozen cache.</summary>
public enum GeocodeIntent
{
    Search,
    Typeahead,
}
