using LupiraGeoApi.Application;
using LupiraGeoApi.Dtos.Geocoding;
using LupiraGeoApi.Mappers;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LupiraGeoApi.Handlers;

public sealed class GeocodeHandler(GeocodingService geocoder)
{
    public async Task<Results<Ok<GeocodeResultDto>, NotFound, UnauthorizedHttpResult>> ReverseAsync(double lat, double lon, CancellationToken ct)
    {
        var hit = await geocoder.ReverseAsync(lat, lon, ct);
        return hit is null ? TypedResults.NotFound() : TypedResults.Ok(hit.ToDto());
    }

    public async Task<Results<Ok<List<GeocodeResultDto>>, UnauthorizedHttpResult>> ForwardAsync(string q, int? limit, CancellationToken ct)
    {
        var result = await geocoder.ForwardAsync(q, limit ?? 5, ct);
        return TypedResults.Ok(result.Hits.Select(h => h.ToDto()).ToList());
    }
}
