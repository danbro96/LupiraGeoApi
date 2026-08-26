using LupiraGeoApi.Auth;
using LupiraGeoApi.Core.Application.Places;
using LupiraGeoApi.Core.Dtos.Curation;
using LupiraGeoApi.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LupiraGeoApi.Handlers;

public sealed class CurationHandler(PlaceOrphanService orphans, CurrentUser user)
{
    public async Task<Results<Ok<List<OrphanCandidateDto>>, ProblemHttpResult, UnauthorizedHttpResult>> FindOrphansAsync(CancellationToken ct) =>
        OpResultMap.OkProblem(await orphans.FindOrphansAsync(ct));

    public async Task<Results<Ok<List<PrunePlaceResultDto>>, ProblemHttpResult, UnauthorizedHttpResult>> PruneAsync(PrunePlacesRequest r, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkProblem(await orphans.PruneAsync(r.PlaceIds, u.Id, ct));
    }
}
