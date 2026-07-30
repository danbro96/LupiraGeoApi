using LupiraGeoApi.Data;
using LupiraGeoApi.Domain;
using LupiraGeoApi.Dtos.Places;
using LupiraGeoApi.Mappers;
using Microsoft.EntityFrameworkCore;

namespace LupiraGeoApi.Application;

/// <summary>Merges a duplicate place into a survivor. The loser becomes a tombstone redirect
/// (<see cref="Place.MergedIntoId"/>) so place ids held by other services keep resolving: names move over as aliases,
/// external ids move, the survivor's missing fields are filled, and saved places re-point. Cross-store by necessity —
/// EF (gazetteer) commits first, then Marten (saved places); not atomic, but re-running the same merge converges.</summary>
// Marten's namespace stays un-imported: its LINQ extensions collide with EF's on the shared IQueryable surface.
public sealed class PlaceMergeService(GeoDbContext db, Marten.IDocumentSession session)
{
    public async Task<OpResult<PlaceDto>> MergeAsync(Guid id, Guid intoPlaceId, Guid actorId, CancellationToken ct = default)
    {
        var loser = await LoadAsync(id, ct);
        if (loser is null) return OpResult<PlaceDto>.NotFound();

        var winner = await LoadAsync(intoPlaceId, ct);
        if (winner is null) return OpResult<PlaceDto>.NotFound();

        // Merging into a tombstone targets its terminal survivor instead.
        var seen = new HashSet<Guid> { loser.Id };
        while (winner.MergedIntoId is { } next)
        {
            if (!seen.Add(winner.Id) || next == loser.Id)
                return OpResult<PlaceDto>.Invalid("Merge would create a redirect cycle.");
            winner = await LoadAsync(next, ct);
            if (winner is null) return OpResult<PlaceDto>.NotFound();
        }

        if (winner.Id == loser.Id) return OpResult<PlaceDto>.Invalid("Cannot merge a place into itself.");
        if (loser.MergedIntoId is { } already)
            return already == winner.Id
                ? OpResult<PlaceDto>.Ok(winner.ToDto())
                : OpResult<PlaceDto>.Conflict("Place is already merged into a different place.");

        foreach (var alias in loser.Aliases)
            AddWinnerAlias(winner, alias.Name, alias.Lang);
        AddWinnerAlias(winner, loser.CanonicalName, lang: null);
        db.PlaceAliases.RemoveRange(loser.Aliases);
        loser.Aliases.Clear();

        foreach (var ext in loser.ExternalIds.ToList())
        {
            if (winner.ExternalIds.Any(w => w.Scheme == ext.Scheme && w.Value == ext.Value))
            {
                db.PlaceExternalIds.Remove(ext);
                continue;
            }

            ext.PlaceId = winner.Id;
            winner.ExternalIds.Add(ext);
        }

        loser.ExternalIds.Clear();

        winner.Location ??= loser.Location;
        winner.FormattedAddress ??= loser.FormattedAddress;
        winner.WithinAreaId ??= loser.WithinAreaId;
        if (winner.Category == PlaceCategory.Unknown) winner.Category = loser.Category;
        winner.Verified |= loser.Verified;

        loser.MergedIntoId = winner.Id;
        db.Record(loser.Id, CurationAction.Merged, actorId, relatedPlaceId: winner.Id);
        await db.SaveChangesAsync(ct);

        var savedPlaces = await Marten.QueryableExtensions.ToListAsync(
            session.Query<SavedPlace>().Where(s => s.PlaceId == loser.Id), ct);
        if (savedPlaces.Count > 0)
        {
            foreach (var saved in savedPlaces)
            {
                saved.PlaceId = winner.Id;
                session.Store(saved);
            }

            await session.SaveChangesAsync(ct);
        }

        return OpResult<PlaceDto>.Ok(winner.ToDto());
    }

    private Task<Place?> LoadAsync(Guid id, CancellationToken ct) =>
        db.Places.Include(p => p.Aliases).Include(p => p.ExternalIds).FirstOrDefaultAsync(p => p.Id == id, ct);

    private void AddWinnerAlias(Place winner, string name, string? lang)
    {
        if (string.Equals(winner.CanonicalName, name, StringComparison.OrdinalIgnoreCase)) return;
        if (winner.Aliases.Any(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase))) return;
        var alias = new PlaceAlias { Id = Guid.NewGuid(), PlaceId = winner.Id, Name = name, Lang = lang };
        winner.Aliases.Add(alias);
        db.PlaceAliases.Add(alias);
    }
}
