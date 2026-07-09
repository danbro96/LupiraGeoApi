using LupiraGeoApi.Application;
using LupiraGeoApi.Auth;
using LupiraGeoApi.Domain;
using LupiraGeoApi.Dtos.Places;
using LupiraGeoApi.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LupiraGeoApi.Handlers;

public sealed class PlacesHandler(PlaceQueryService places, CurrentUser user)
{
    public async Task<Results<Ok<List<PlaceDto>>, ProblemHttpResult, UnauthorizedHttpResult>> SearchAsync(
        string? q, PlaceCategory? category, PlaceKind? kind, Guid? withinAreaId,
        double? nearLat, double? nearLon, double? radiusM, double[]? bbox, int? limit, CancellationToken ct) =>
        OpResultMap.OkProblem(await places.SearchAsync(q, category, kind, withinAreaId, nearLat, nearLon, radiusM, bbox, limit, ct));

    public async Task<Results<Ok<PlaceDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> GetAsync(Guid id, CancellationToken ct) =>
        OpResultMap.OkNotFoundProblem(await places.GetAsync(id, ct));

    public async Task<Results<Ok<PlaceDto>, ProblemHttpResult, UnauthorizedHttpResult>> CreateAsync(CreatePlaceRequest r, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkProblem(await places.CreateAsync(r, u.Id, ct));
    }

    public async Task<Results<Ok<PlaceDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> UpdateAsync(Guid id, UpdatePlaceRequest r, CancellationToken ct) =>
        OpResultMap.OkNotFoundProblem(await places.UpdateAsync(id, r, ct));

    public async Task<Results<Ok<ResolvePlaceResponse>, ProblemHttpResult, UnauthorizedHttpResult>> ResolveAsync(ResolvePlaceRequest r, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkProblem(await places.ResolveAsync(r.Text, u.Id, ct));
    }
}
