using Lupira.Hosting.Problems;
using LupiraGeoApi.Core.Application.Geocoding;
using LupiraGeoApi.Core.Dtos.Geocoding;
using LupiraGeoApi.Core.Mappers;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LupiraGeoApi.Handlers;

public sealed class GeocodeHandler(GeocodingService geocoder, AddressSearchService search)
{
    public async Task<Results<Ok<GeocodeResultDto>, NotFound, UnauthorizedHttpResult>> ReverseAsync(
        double lat, double lon, ReversePrecision precision, CancellationToken ct)
    {
        var hit = await geocoder.ReverseAsync(lat, lon, precision, ct);
        return hit is null ? TypedResults.NotFound() : TypedResults.Ok(hit.ToDto());
    }

    public async Task<Results<Ok<List<GeocodeResultDto>>, ProblemHttpResult, UnauthorizedHttpResult>> ForwardAsync(
        string? q, string? street, string? postcode, string? locality, string? countryCode,
        GeocodeIntent intent, double? nearLat, double? nearLon, int? limit, CancellationToken ct) =>
        OpResultMap.OkProblem(await search.SearchAsync(q, street, postcode, locality, countryCode, intent, nearLat, nearLon, limit, ct));
}
