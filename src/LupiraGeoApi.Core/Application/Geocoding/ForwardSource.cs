namespace LupiraGeoApi.Core.Application.Geocoding;

/// <summary>A forward-geocode backend. <c>Resolver</c> is the cached regional-then-public geocoder;
/// <c>RegionalResolver</c> the regional instance alone, uncached.</summary>
public enum ForwardSource
{
    Resolver,
    RegionalResolver,
    Autocomplete,
}
