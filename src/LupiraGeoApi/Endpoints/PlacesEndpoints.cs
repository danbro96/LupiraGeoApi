using LupiraGeoApi.Domain;
using LupiraGeoApi.Dtos.Places;
using LupiraGeoApi.Handlers;

namespace LupiraGeoApi.Endpoints;

public static class PlacesEndpoints
{
    public static IEndpointRouteBuilder MapPlaces(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/places").RequireAuthorization("ApiPolicy").WithTags("Places");

        group.MapGet("/", (string? q, PlaceCategory? category, PlaceKind? kind, Guid? withinAreaId,
                double? nearLat, double? nearLon, double? radiusM, double[]? bbox, int? limit,
                PlacesHandler h, CancellationToken ct) =>
                h.SearchAsync(q, category, kind, withinAreaId, nearLat, nearLon, radiusM, bbox, limit, ct))
            .WithName("SearchPlaces")
            .WithSummary("Search the gazetteer: text (q, trigram), category/kind, containment (withinAreaId), and spatial — proximity (nearLat+nearLon[+radiusM], returns distanceM) or viewport (bbox=minLon&bbox=minLat&bbox=maxLon&bbox=maxLat).")
            .Produces<List<PlaceDto>>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/{id:guid}", (Guid id, PlacesHandler h, CancellationToken ct) => h.GetAsync(id, ct))
            .WithName("GetPlace")
            .WithSummary("A single place with its aliases, external ids, and containment chain (outermost→innermost).")
            .Produces<PlaceDto>(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/", (CreatePlaceRequest r, PlacesHandler h, CancellationToken ct) => h.CreateAsync(r, ct))
            .WithName("CreatePlace")
            .WithSummary("Create a user place directly (name + optional coordinates/category).")
            .Produces<PlaceDto>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status401Unauthorized);

        group.MapPatch("/{id:guid}", (Guid id, UpdatePlaceRequest r, PlacesHandler h, CancellationToken ct) => h.UpdateAsync(id, r, ct))
            .WithName("UpdatePlace")
            .WithSummary("Curate a place: rename, recategorize, or verify.")
            .Produces<PlaceDto>(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/resolve", (ResolvePlaceRequest r, PlacesHandler h, CancellationToken ct) => h.ResolveAsync(r, ct))
            .WithName("ResolvePlace")
            .WithSummary("Resolve free-text to a place id — match an existing entry, geocode, or provisionally create. Used by upstream services (e.g. LupiraCalApi) to anchor a location string.")
            .Produces<ResolvePlaceResponse>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status401Unauthorized);

        return app;
    }
}
