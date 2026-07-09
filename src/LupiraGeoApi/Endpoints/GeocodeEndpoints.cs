using LupiraGeoApi.Dtos.Geocoding;
using LupiraGeoApi.Handlers;

namespace LupiraGeoApi.Endpoints;

public static class GeocodeEndpoints
{
    public static IEndpointRouteBuilder MapGeocode(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/geocode").RequireAuthorization("ApiPolicy").WithTags("Geocoding");

        group.MapGet("/reverse", (double lat, double lon, GeocodeHandler h, CancellationToken ct) => h.ReverseAsync(lat, lon, ct))
            .WithName("ReverseGeocode")
            .WithSummary("Coordinate → coarse label + structured address (cached; coordinates quantized to a ~100 m grid).")
            .Produces<GeocodeResultDto>(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/forward", (string q, int? limit, GeocodeHandler h, CancellationToken ct) => h.ForwardAsync(q, limit, ct))
            .WithName("ForwardGeocode")
            .WithSummary("Text → candidate coordinates + structured address (cached).")
            .Produces<List<GeocodeResultDto>>(StatusCodes.Status200OK).Produces(StatusCodes.Status401Unauthorized);

        return app;
    }
}
