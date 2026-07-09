using LupiraGeoApi.Dtos.SavedPlaces;
using LupiraGeoApi.Handlers;

namespace LupiraGeoApi.Endpoints;

public static class SavedPlacesEndpoints
{
    public static IEndpointRouteBuilder MapSavedPlaces(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/me/places").RequireAuthorization("ApiPolicy").WithTags("SavedPlaces");

        group.MapGet("/", (SavedPlacesHandler h, CancellationToken ct) => h.ListAsync(ct))
            .WithName("ListSavedPlaces")
            .WithSummary("The caller's saved places / personal labels (favorites first).")
            .Produces<List<SavedPlaceDto>>(StatusCodes.Status200OK).Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/", (CreateSavedPlaceRequest r, SavedPlacesHandler h, CancellationToken ct) => h.CreateAsync(r, ct))
            .WithName("CreateSavedPlace")
            .WithSummary("Save a place with a personal label (references a gazetteer place, or a raw coordinate).")
            .Produces<SavedPlaceDto>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status401Unauthorized);

        group.MapPatch("/{id:guid}", (Guid id, UpdateSavedPlaceRequest r, SavedPlacesHandler h, CancellationToken ct) => h.UpdateAsync(id, r, ct))
            .WithName("UpdateSavedPlace")
            .WithSummary("Rename, re-icon, or (un)favorite a saved place.")
            .Produces<SavedPlaceDto>(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status401Unauthorized);

        group.MapDelete("/{id:guid}", (Guid id, SavedPlacesHandler h, CancellationToken ct) => h.DeleteAsync(id, ct))
            .WithName("DeleteSavedPlace")
            .WithSummary("Remove a saved place.")
            .Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status401Unauthorized);

        return app;
    }
}
