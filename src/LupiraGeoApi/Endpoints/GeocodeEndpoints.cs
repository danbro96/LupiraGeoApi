using LupiraGeoApi.Core.Application.Geocoding;
using LupiraGeoApi.Core.Dtos.Geocoding;
using LupiraGeoApi.Handlers;

namespace LupiraGeoApi.Endpoints;

public static class GeocodeEndpoints
{
    public static IEndpointRouteBuilder MapGeocode(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/geocode").RequireAuthorization("ApiPolicy").WithTags("Geocoding");

        group.MapGet("/reverse", (double lat, double lon, ReversePrecision? precision, GeocodeHandler h, CancellationToken ct) =>
                h.ReverseAsync(lat, lon, precision ?? ReversePrecision.Area, ct))
            .WithName("ReverseGeocode")
            .WithSummary("Coordinate → label + structured address (cached). precision=Area (default) shares one answer per ~100 m cell; precision=Building per ~11 m cell at building zoom.")
            .Produces<GeocodeResultDto>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/forward", (string? q, string? street, string? postcode, string? locality, string? countryCode,
                GeocodeIntent? intent, double? nearLat, double? nearLon, int? limit, GeocodeHandler h, CancellationToken ct) =>
                h.ForwardAsync(q, street, postcode, locality, countryCode, intent ?? GeocodeIntent.Search, nearLat, nearLon, limit, ct))
            .WithName("ForwardGeocode")
            .WithSummary("Address/place → candidate coordinates + structured address. Pass q (free text) or structured fields (street with optional house number, postcode, locality, countryCode; street, or postcode+locality, required). intent=Search (default) resolves via the canonical geocoder (cached) with address autocomplete as a second source; intent=Typeahead asks the address autocomplete only, uncached, biased toward nearLat/nearLon. limit 1–20 (default 5).")
            .Produces<List<GeocodeResultDto>>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status400BadRequest);

        return app;
    }
}
