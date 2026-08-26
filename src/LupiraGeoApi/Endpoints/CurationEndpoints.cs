using LupiraGeoApi.Core.Dtos.Curation;
using LupiraGeoApi.Handlers;

namespace LupiraGeoApi.Endpoints;

/// <summary>Gazetteer maintenance (LAN-only via <see cref="LanOnlyExposure"/> — tunnelled requests 404). Documented in
/// OpenAPI because the SPA's client codegen reads the spec; the fence is runtime, not spec secrecy.</summary>
public static class CurationEndpoints
{
    public static IEndpointRouteBuilder MapCuration(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/curation").RequireAuthorization("ApiPolicy").WithTags("Curation");

        group.MapGet("/orphans", (CurationHandler h, CancellationToken ct) => h.FindOrphansAsync(ct))
            .WithName("FindOrphanPlaces")
            .WithSummary("Live places nothing references — cross-checked against contact addresses, calendar items (live + deleted counted separately), and saved places. Fails 400 when a reference source is unreachable (never declares orphans on partial data).")
            .Produces<List<OrphanCandidateDto>>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/prune", (PrunePlacesRequest r, CurationHandler h, CancellationToken ct) => h.PruneAsync(r, ct))
            .WithName("PrunePlaces")
            .WithSummary("Soft-delete orphan places (max 100 per call). References are re-checked per id at prune time; a place referenced since the find returns Referenced and is left alone. Places referenced only by soft-deleted calendar items are never pruned here — use DELETE /places/{id} to override.")
            .Produces<List<PrunePlaceResultDto>>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status401Unauthorized);

        return app;
    }
}
