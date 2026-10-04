using Lupira.Hosting.Problems;
using LupiraGeoApi.Auth;
using LupiraGeoApi.Core.Application.Places;
using LupiraGeoApi.Core.Dtos.Curation;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LupiraGeoApi.Handlers;

public sealed class CurationHandler(PlaceOrphanService orphans, PlaceAreaSweepService areas, CurrentUser user)
{
    public async Task<Results<Ok<List<AreaReclassificationDto>>, ProblemHttpResult, UnauthorizedHttpResult>> ClassifyAreasAsync(bool apply, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkProblem(await areas.SweepAsync(apply, u.Id, ct));
    }

    public async Task<Results<Ok<List<OrphanCandidateDto>>, ProblemHttpResult, UnauthorizedHttpResult>> FindOrphansAsync(CancellationToken ct) =>
        OpResultMap.OkProblem(await orphans.FindOrphansAsync(ct));

    public async Task<Results<Ok<List<PrunePlaceResultDto>>, ProblemHttpResult, UnauthorizedHttpResult>> PruneAsync(PrunePlacesRequest r, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkProblem(await orphans.PruneAsync(r.PlaceIds, u.Id, ct));
    }
}
