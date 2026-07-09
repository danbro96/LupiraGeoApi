using LupiraGeoApi.Application;
using LupiraGeoApi.Auth;
using LupiraGeoApi.Dtos.SavedPlaces;
using LupiraGeoApi.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LupiraGeoApi.Handlers;

public sealed class SavedPlacesHandler(SavedPlaceService saved, CurrentUser user)
{
    public async Task<Results<Ok<List<SavedPlaceDto>>, ProblemHttpResult, UnauthorizedHttpResult>> ListAsync(CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkProblem(await saved.ListAsync(u.Id, ct));
    }

    public async Task<Results<Ok<SavedPlaceDto>, ProblemHttpResult, UnauthorizedHttpResult>> CreateAsync(CreateSavedPlaceRequest r, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkProblem(await saved.CreateAsync(u.Id, r, ct));
    }

    public async Task<Results<Ok<SavedPlaceDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> UpdateAsync(Guid id, UpdateSavedPlaceRequest r, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await saved.UpdateAsync(u.Id, id, r, ct));
    }

    public async Task<Results<NoContent, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> DeleteAsync(Guid id, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.NoContentNotFoundProblem(await saved.DeleteAsync(u.Id, id, ct));
    }
}
