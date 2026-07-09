using LupiraGeoApi.Domain;
using LupiraGeoApi.Dtos.AdminAreas;
using LupiraGeoApi.Handlers;

namespace LupiraGeoApi.Endpoints;

public static class AdminAreasEndpoints
{
    public static IEndpointRouteBuilder MapAdminAreas(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/admin-areas").RequireAuthorization("ApiPolicy").WithTags("AdminAreas");

        group.MapGet("/", (AdminLevel? level, Guid? withinAreaId, string? q, int? limit, AdminAreasHandler h, CancellationToken ct) =>
                h.ListAsync(level, withinAreaId, q, limit, ct))
            .WithName("ListAdminAreas")
            .WithSummary("Browse the administrative containment tree: filter by level (Country/Region/Locality), parent (withinAreaId), or name (q).")
            .Produces<List<AdminAreaDto>>(StatusCodes.Status200OK).Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/{id:guid}", (Guid id, AdminAreasHandler h, CancellationToken ct) => h.GetAsync(id, ct))
            .WithName("GetAdminArea")
            .WithSummary("A single administrative area.")
            .Produces<AdminAreaDto>(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status401Unauthorized);

        return app;
    }
}
