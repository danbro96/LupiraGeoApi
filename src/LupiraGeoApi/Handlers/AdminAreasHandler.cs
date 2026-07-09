using LupiraGeoApi.Application;
using LupiraGeoApi.Domain;
using LupiraGeoApi.Dtos.AdminAreas;
using LupiraGeoApi.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LupiraGeoApi.Handlers;

public sealed class AdminAreasHandler(AdminAreaService areas)
{
    public async Task<Results<Ok<List<AdminAreaDto>>, ProblemHttpResult, UnauthorizedHttpResult>> ListAsync(
        AdminLevel? level, Guid? withinAreaId, string? q, int? limit, CancellationToken ct) =>
        OpResultMap.OkProblem(await areas.ListAsync(level, withinAreaId, q, limit, ct));

    public async Task<Results<Ok<AdminAreaDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> GetAsync(Guid id, CancellationToken ct) =>
        OpResultMap.OkNotFoundProblem(await areas.GetAsync(id, ct));
}
