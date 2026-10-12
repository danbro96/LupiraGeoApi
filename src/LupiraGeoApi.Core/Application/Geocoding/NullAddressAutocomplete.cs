namespace LupiraGeoApi.Core.Application.Geocoding;

public sealed class NullAddressAutocomplete : IAddressAutocomplete
{
    public bool IsConfigured => false;

    public Task<ForwardResult> SuggestAsync(AddressQuery query, GeoPoint? near, int limit, CancellationToken ct = default) =>
        Task.FromResult(ForwardResult.Empty);
}
