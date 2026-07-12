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

        group.MapGet("/suggest", (string q, int? limit, PlacesHandler h, CancellationToken ct) => h.SuggestAsync(q, limit, ct))
            .WithName("SuggestPlaces")
            .WithSummary("Typeahead: trigram-ranked suggestions over places (names + aliases) and AdminArea localities, discriminated by type.")
            .Produces<List<PlaceSuggestionDto>>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/by-external/{scheme}/{**value}", (ExternalScheme scheme, string value, PlacesHandler h, CancellationToken ct) =>
                h.GetByExternalIdAsync(scheme, value, ct))
            .WithName("GetPlaceByExternalId")
            .WithSummary("Look a place up by an external gazetteer key, e.g. /places/by-external/Osm/node/123.")
            .Produces<PlaceDto>(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/{id:guid}", (Guid id, PlacesHandler h, CancellationToken ct) => h.GetAsync(id, ct))
            .WithName("GetPlace")
            .WithSummary("A single place with its aliases, external ids, and containment chain (outermost→innermost). Follows merge redirects.")
            .Produces<PlaceDto>(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/", (CreatePlaceRequest r, PlacesHandler h, CancellationToken ct) => h.CreateAsync(r, ct))
            .WithName("CreatePlace")
            .WithSummary("Create a user place directly (name + optional coordinates/category).")
            .Produces<PlaceDto>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status401Unauthorized);

        group.MapPatch("/{id:guid}", (Guid id, UpdatePlaceRequest r, PlacesHandler h, CancellationToken ct) => h.UpdateAsync(id, r, ct))
            .WithName("UpdatePlace")
            .WithSummary("Curate a place: rename, recategorize, or verify.")
            .Produces<PlaceDto>(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/{id:guid}/aliases", (Guid id, AddAliasRequest r, PlacesHandler h, CancellationToken ct) => h.AddAliasAsync(id, r, ct))
            .WithName("AddPlaceAlias")
            .WithSummary("Add an alternate name (optional language tag) to a place; resolve and suggest match aliases.")
            .Produces<PlaceDto>(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict).Produces(StatusCodes.Status401Unauthorized);

        group.MapDelete("/{id:guid}/aliases/{aliasId:guid}", (Guid id, Guid aliasId, PlacesHandler h, CancellationToken ct) => h.RemoveAliasAsync(id, aliasId, ct))
            .WithName("RemovePlaceAlias")
            .WithSummary("Remove an alias from a place.")
            .Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/{id:guid}/external-ids", (Guid id, AddExternalIdRequest r, PlacesHandler h, CancellationToken ct) => h.AddExternalIdAsync(id, r, ct))
            .WithName("AddPlaceExternalId")
            .WithSummary("Attach an external gazetteer id (scheme+value) to a place; multiple ids per scheme are allowed. 409 if the id already belongs to another place (merge those instead) or is already on this place.")
            .Produces<PlaceDto>(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict).Produces(StatusCodes.Status401Unauthorized);

        group.MapDelete("/{id:guid}/external-ids/{scheme}/{**value}", (Guid id, ExternalScheme scheme, string value, PlacesHandler h, CancellationToken ct) => h.RemoveExternalIdAsync(id, scheme, value, ct))
            .WithName("RemovePlaceExternalId")
            .WithSummary("Detach an external id (scheme + full value, e.g. /external-ids/Osm/way/6601741) from a place.")
            .Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/{id:guid}/merge", (Guid id, MergePlaceRequest r, PlacesHandler h, CancellationToken ct) => h.MergeAsync(id, r, ct))
            .WithName("MergePlace")
            .WithSummary("Merge a duplicate into the survivor (intoPlaceId): names become aliases, external ids and saved places move over, and the duplicate id keeps resolving via a tombstone redirect.")
            .Produces<PlaceDto>(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict).Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/{id:guid}/regeocode", (Guid id, PlacesHandler h, CancellationToken ct) => h.RegeocodeAsync(id, ct))
            .WithName("RegeocodePlace")
            .WithSummary("Re-geocode a place from its address/name and attach coordinates, containment, and OSM id — heals a coordinate-less stub or refreshes a stale fix. 400 on a no-hit or transient geocoder outage; the place is left unchanged.")
            .Produces<PlaceDto>(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status401Unauthorized);

        group.MapDelete("/{id:guid}", (Guid id, PlacesHandler h, CancellationToken ct) => h.DeleteAsync(id, ct))
            .WithName("DeletePlace")
            .WithSummary("Soft-delete a bad entry (e.g. a wrong geocode) with no valid survivor to merge into: tombstoned, so reads 404 and search/resolve exclude it, but the row stays for the audit trail. Idempotent.")
            .Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/resolve", (ResolvePlaceRequest r, PlacesHandler h, CancellationToken ct) => h.ResolveAsync(r, ct))
            .WithName("ResolvePlace")
            .WithSummary("Resolve free-text to a place id — match an existing entry, geocode, or provisionally create. Used by upstream services (e.g. LupiraCalApi) to anchor a location string.")
            .Produces<ResolvePlaceResponse>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/resolve:batch", (ResolvePlacesBatchRequest r, PlacesHandler h, CancellationToken ct) => h.ResolveBatchAsync(r, ct))
            .WithName("ResolvePlacesBatch")
            .WithSummary("Bulk resolve (max 50 texts); responses align index-for-index with the input.")
            .Produces<List<ResolvePlaceResponse>>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status401Unauthorized);

        return app;
    }
}
