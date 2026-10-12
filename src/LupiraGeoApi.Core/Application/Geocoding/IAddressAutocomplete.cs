namespace LupiraGeoApi.Core.Application.Geocoding;

/// <summary>Search-as-you-type address and place candidates. Never cached: suggestions are previews, and a pick is
/// resolved through the canonical geocoder by OSM identity.</summary>
public interface IAddressAutocomplete
{
    bool IsConfigured { get; }

    Task<ForwardResult> SuggestAsync(AddressQuery query, GeoPoint? near, int limit, CancellationToken ct = default);
}
