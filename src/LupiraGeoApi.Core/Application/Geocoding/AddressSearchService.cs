using Lupira.Results;
using LupiraGeoApi.Core.Dtos.Geocoding;
using LupiraGeoApi.Core.Mappers;

namespace LupiraGeoApi.Core.Application.Geocoding;

/// <summary>Forward geocoding by intent (see <see cref="ForwardRouting"/>): the first backend with hits answers.</summary>
public sealed class AddressSearchService(GeocodingService geocoder, IAddressAutocomplete autocomplete)
{
    public const int MaxLimit = 20;

    public async Task<OpResult<List<GeocodeResultDto>>> SearchAsync(
        string? q, string? street, string? postcode, string? locality, string? countryCode,
        GeocodeIntent intent, double? nearLat, double? nearLon, int? limit, CancellationToken ct = default)
    {
        var parsed = AddressQuery.Create(q, street, postcode, locality, countryCode);
        if (!parsed.IsOk) return OpResult<List<GeocodeResultDto>>.Invalid(parsed.Error!);
        if (nearLat.HasValue != nearLon.HasValue)
            return OpResult<List<GeocodeResultDto>>.Invalid("nearLat and nearLon must be supplied together.");
        if (nearLat is < -90 or > 90 || nearLon is < -180 or > 180)
            return OpResult<List<GeocodeResultDto>>.Invalid("nearLat must be within ±90 and nearLon within ±180.");

        var query = parsed.Value!;
        var near = nearLat is { } lat && nearLon is { } lon ? new GeoPoint(lat, lon) : (GeoPoint?) null;
        var take = Math.Clamp(limit ?? 5, 1, MaxLimit);
        foreach (var source in ForwardRouting.Plan(intent, autocomplete.IsConfigured))
        {
            var hits = source switch
            {
                ForwardSource.Resolver => (await geocoder.ForwardAsync(query, take, ct: ct)).Hits,
                ForwardSource.RegionalResolver => (await geocoder.ForwardRegionalAsync(query, take, ct)).Hits,
                _ => (await autocomplete.SuggestAsync(query, near, take, ct)).Hits
                    .Where(h => ForwardHitFilter.MatchesPostcode(query, h)).ToList(),
            };
            if (hits.Count > 0) return OpResult<List<GeocodeResultDto>>.Ok([.. hits.Take(take).Select(h => h.ToDto())]);
        }

        return OpResult<List<GeocodeResultDto>>.Ok([]);
    }
}
