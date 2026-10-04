using LupiraGeoApi.Core.Domain;
using LupiraGeoApi.Core.Dtos.Places;
using LupiraGeoApi.Handlers;

namespace LupiraGeoApi.Endpoints;

public static class PlacesEndpoints
{
    public static IEndpointRouteBuilder MapPlaces(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/places").RequireAuthorization("ApiPolicy").WithTags("Places");

        group.MapGet("/", (string? q, PlaceCategory? category, PlaceKind? kind, Guid? withinAreaId,
                bool? hasCoordinates, PlaceSource? source, bool? verified,
                double? nearLat, double? nearLon, double? radiusM, double[]? bbox, int? limit,
                PlacesHandler h, CancellationToken ct) =>
                h.SearchAsync(q, category, kind, withinAreaId, hasCoordinates, source, verified, nearLat, nearLon, radiusM, bbox, limit, ct))
            .WithName("SearchPlaces")
            .WithSummary("Search the gazetteer: text (q, trigram), category/kind, containment (withinAreaId), curation state (hasCoordinates/source/verified — hasCoordinates=false lists unlocated stubs), and spatial — proximity (nearLat+nearLon[+radiusM], returns distanceM) or viewport (bbox=minLon&bbox=minLat&bbox=maxLon&bbox=maxLat).")
            .Produces<List<PlaceDto>>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/suggest", (string q, int? limit, PlacesHandler h, CancellationToken ct) => h.SuggestAsync(q, limit, ct))
            .WithName("SuggestPlaces")
            .WithSummary("Typeahead: trigram-ranked suggestions over places (names + aliases) and AdminArea localities, discriminated by type.")
            .Produces<List<PlaceSuggestionDto>>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/duplicates", (double? radiusM, double? minSimilarity, int? limit, PlacesHandler h, CancellationToken ct) =>
                h.DuplicatesAsync(radiusM, minSimilarity, limit, ct))
            .WithName("FindDuplicatePlaces")
            .WithSummary("Duplicate candidates for the curation pass: places sharing a match key (SameName), plus co-located places with similar names (CoLocated, radiusM default 25, minSimilarity default 0.3). Read-only — merging is the caller's call.")
            .Produces<List<DuplicateClusterDto>>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/by-external/{scheme}/{**value}", (ExternalScheme scheme, string value, PlacesHandler h, CancellationToken ct) =>
                h.GetByExternalIdAsync(scheme, value, ct))
            .WithName("GetPlaceByExternalId")
            .WithSummary("Look a place up by an external gazetteer key, e.g. /places/by-external/Osm/node/123.")
            .Produces<PlaceDto>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/{id:guid}/history", (Guid id, PlacesHandler h, CancellationToken ct) => h.HistoryAsync(id, ct))
            .WithName("GetPlaceHistory")
            .WithSummary("The append-only curation log for a place, oldest first. Readable for tombstoned/merged places; 404 only for unknown ids.")
            .Produces<List<CurationEventDto>>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}", (Guid id, PlacesHandler h, CancellationToken ct) => h.GetAsync(id, ct))
            .WithName("GetPlace")
            .WithSummary("A single place with its aliases, external ids, and containment chain (outermost→innermost). Follows merge redirects.")
            .Produces<PlaceDto>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", (CreatePlaceRequest r, PlacesHandler h, CancellationToken ct) => h.CreateAsync(r, ct))
            .WithName("CreatePlace")
            .WithSummary("Create a user place directly (name + optional coordinates/category).")
            .Produces<PlaceDto>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPatch("/{id:guid}", (Guid id, UpdatePlaceRequest r, PlacesHandler h, CancellationToken ct) => h.UpdateAsync(id, r, ct))
            .WithName("UpdatePlace")
            .WithSummary("Curate a place: rename, recategorize, or verify.")
            .Produces<PlaceDto>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/{id:guid}/aliases", (Guid id, AddAliasRequest r, PlacesHandler h, CancellationToken ct) => h.AddAliasAsync(id, r, ct))
            .WithName("AddPlaceAlias")
            .WithSummary("Add an alternate name (optional language tag) to a place; resolve and suggest match aliases.")
            .Produces<PlaceDto>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{id:guid}/aliases/{aliasId:guid}", (Guid id, Guid aliasId, PlacesHandler h, CancellationToken ct) => h.RemoveAliasAsync(id, aliasId, ct))
            .WithName("RemovePlaceAlias")
            .WithSummary("Remove an alias from a place.")
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/external-ids", (Guid id, AddExternalIdRequest r, PlacesHandler h, CancellationToken ct) => h.AddExternalIdAsync(id, r, ct))
            .WithName("AddPlaceExternalId")
            .WithSummary("Attach an external gazetteer id (scheme+value) to a place; multiple ids per scheme are allowed. 409 if the id already belongs to another place (merge those instead) or is already on this place.")
            .Produces<PlaceDto>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{id:guid}/external-ids/{scheme}/{**value}", (Guid id, ExternalScheme scheme, string value, PlacesHandler h, CancellationToken ct) => h.RemoveExternalIdAsync(id, scheme, value, ct))
            .WithName("RemovePlaceExternalId")
            .WithSummary("Detach an external id (scheme + full value, e.g. /external-ids/Osm/way/6601741) from a place.")
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/{id:guid}/merge", (Guid id, MergePlaceRequest r, PlacesHandler h, CancellationToken ct) => h.MergeAsync(id, r, ct))
            .WithName("MergePlace")
            .WithSummary("Merge a duplicate into the survivor (intoPlaceId): names become aliases, saved places move over, and the duplicate id keeps resolving via a tombstone redirect. The survivor's own fields win; the duplicate only fills what it lacks (coordinates, address, containment and OSM id only when the survivor has no coordinates).")
            .Produces<PlaceDto>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/regeocode", (Guid id, bool? force, PlacesHandler h, CancellationToken ct) => h.RegeocodeAsync(id, force ?? false, ct))
            .WithName("RegeocodePlace")
            .WithSummary("Re-geocode a place from its address/name and attach coordinates, containment, and OSM id — heals a coordinate-less stub or refreshes a stale fix. A frozen empty geocode answer is always re-asked; force=true also bypasses and overwrites frozen hits. 400 on a no-hit or transient geocoder outage; the place is left unchanged.")
            .Produces<PlaceDto>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/regeocode:batch", (RegeocodePlacesBatchRequest r, PlacesHandler h, CancellationToken ct) => h.RegeocodeBatchAsync(r, ct))
            .WithName("RegeocodePlacesBatch")
            .WithSummary("Regeocode up to 50 places in one call — the bulk healing path for unlocated stubs. Per-item status (Healed/NoHit/Unavailable/Conflict/NotFound); a failure never aborts the rest. Frozen empty geocode answers are always re-asked; force=true also bypasses frozen hits.")
            .Produces<List<RegeocodePlaceResultDto>>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapDelete("/{id:guid}", (Guid id, PlacesHandler h, CancellationToken ct) => h.DeleteAsync(id, ct))
            .WithName("DeletePlace")
            .WithSummary("Soft-delete a bad entry (e.g. a wrong geocode) with no valid survivor to merge into: tombstoned, so reads 404 and search/resolve exclude it, but the row stays for the audit trail. Idempotent.")
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/lookup", (LookupPlacesRequest r, PlacesHandler h, CancellationToken ct) => h.LookupAsync(r, ct))
            .WithName("LookupPlaces")
            .WithSummary("Bulk get-by-ids (max 200) — hydrate stored place ids into coordinates in one call. Responses align index-for-index; a null place means unknown or deleted, a merged id returns the survivor. Containment is omitted (use GET /places/{id} for detail).")
            .Produces<List<PlaceLookupItemDto>>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/resolve", (ResolvePlaceRequest r, PlacesHandler h, CancellationToken ct) => h.ResolveAsync(r, ct))
            .WithName("ResolvePlace")
            .WithSummary("Resolve free-text to a place id — match an existing entry, geocode, or provisionally create. Used by upstream services (e.g. LupiraCalApi) to anchor a location string.")
            .Produces<ResolvePlaceResponse>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/resolve:batch", (ResolvePlacesBatchRequest r, PlacesHandler h, CancellationToken ct) => h.ResolveBatchAsync(r, ct))
            .WithName("ResolvePlacesBatch")
            .WithSummary("Bulk resolve (max 50 texts); responses align index-for-index with the input.")
            .Produces<List<ResolvePlaceResponse>>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/from-geocode", (CreatePlaceFromGeocodeRequest r, PlacesHandler h, CancellationToken ct) => h.CreateFromGeocodeAsync(r, ct))
            .WithName("CreatePlaceFromGeocode")
            .WithSummary("Create/dedupe a place from one specific forward-geocode hit the user picked (query + OSM identity). Reuses the frozen geocode cache — no extra geocoder call. 400 if the hit is not among the query's geocode results.")
            .Produces<ResolvePlaceResponse>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }
}
