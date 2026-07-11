using LupiraGeoApi.Application;
using LupiraGeoApi.Auth;
using LupiraGeoApi.Domain;
using LupiraGeoApi.Dtos.Places;
using LupiraGeoApi.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LupiraGeoApi.Handlers;

public sealed class PlacesHandler(PlaceQueryService places, PlaceMergeService merges, CurrentUser user)
{
    public async Task<Results<Ok<List<PlaceDto>>, ProblemHttpResult, UnauthorizedHttpResult>> SearchAsync(
        string? q, PlaceCategory? category, PlaceKind? kind, Guid? withinAreaId,
        double? nearLat, double? nearLon, double? radiusM, double[]? bbox, int? limit, CancellationToken ct) =>
        OpResultMap.OkProblem(await places.SearchAsync(q, category, kind, withinAreaId, nearLat, nearLon, radiusM, bbox, limit, ct));

    public async Task<Results<Ok<List<PlaceSuggestionDto>>, ProblemHttpResult, UnauthorizedHttpResult>> SuggestAsync(
        string q, int? limit, CancellationToken ct) =>
        OpResultMap.OkProblem(await places.SuggestAsync(q, limit, ct));

    public async Task<Results<Ok<PlaceDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> GetAsync(Guid id, CancellationToken ct) =>
        OpResultMap.OkNotFoundProblem(await places.GetAsync(id, ct));

    public async Task<Results<Ok<PlaceDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> GetByExternalIdAsync(
        ExternalScheme scheme, string value, CancellationToken ct) =>
        OpResultMap.OkNotFoundProblem(await places.GetByExternalIdAsync(scheme, value, ct));

    public async Task<Results<Ok<PlaceDto>, ProblemHttpResult, UnauthorizedHttpResult>> CreateAsync(CreatePlaceRequest r, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkProblem(await places.CreateAsync(r, u.Id, ct));
    }

    public async Task<Results<Ok<PlaceDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> UpdateAsync(Guid id, UpdatePlaceRequest r, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await places.UpdateAsync(id, r, u.Id, ct));
    }

    public async Task<Results<Ok<PlaceDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> AddAliasAsync(
        Guid id, AddAliasRequest r, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await places.AddAliasAsync(id, r, u.Id, ct));
    }

    public async Task<Results<NoContent, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> RemoveAliasAsync(
        Guid id, Guid aliasId, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.NoContentNotFoundProblem(await places.RemoveAliasAsync(id, aliasId, u.Id, ct));
    }

    public async Task<Results<Ok<PlaceDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> MergeAsync(
        Guid id, MergePlaceRequest r, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await merges.MergeAsync(id, r.IntoPlaceId, u.Id, ct));
    }

    public async Task<Results<Ok<ResolvePlaceResponse>, ProblemHttpResult, UnauthorizedHttpResult>> ResolveAsync(ResolvePlaceRequest r, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkProblem(await places.ResolveAsync(r.Text, u.Id, ct));
    }

    public async Task<Results<Ok<List<ResolvePlaceResponse>>, ProblemHttpResult, UnauthorizedHttpResult>> ResolveBatchAsync(
        ResolvePlacesBatchRequest r, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkProblem(await places.ResolveBatchAsync(r.Texts, u.Id, ct));
    }
}
