using LupiraGeoApi.Domain;
using LupiraGeoApi.Dtos.SavedPlaces;
using LupiraGeoApi.Mappers;
using Marten;

namespace LupiraGeoApi.Application;

/// <summary>Per-principal saved places / personal labels (Marten, <c>geo_user</c>). Every operation is scoped to the
/// calling principal; a saved place owned by someone else reads as <c>NotFound</c>.</summary>
public sealed class SavedPlaceService(IDocumentSession session)
{
    public async Task<OpResult<List<SavedPlaceDto>>> ListAsync(Guid principalId, CancellationToken ct = default)
    {
        var saved = await session.Query<SavedPlace>()
            .Where(s => s.PrincipalId == principalId)
            .OrderByDescending(s => s.IsFavorite).ThenBy(s => s.Label)
            .ToListAsync(ct);
        return OpResult<List<SavedPlaceDto>>.Ok(saved.Select(s => s.ToDto()).ToList());
    }

    public async Task<OpResult<SavedPlaceDto>> CreateAsync(Guid principalId, CreateSavedPlaceRequest r, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(r.Label)) return OpResult<SavedPlaceDto>.Invalid("Label is required.");
        if (r.PlaceId is null && r is not { Latitude: not null, Longitude: not null })
            return OpResult<SavedPlaceDto>.Invalid("A saved place needs either a placeId or a latitude/longitude.");

        var saved = new SavedPlace
        {
            Id = Guid.NewGuid(),
            PrincipalId = principalId,
            PlaceId = r.PlaceId,
            RawLat = r.Latitude,
            RawLon = r.Longitude,
            Label = r.Label.Trim(),
            Icon = r.Icon,
            IsFavorite = r.IsFavorite,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        session.Store(saved);
        await session.SaveChangesAsync(ct);
        return OpResult<SavedPlaceDto>.Ok(saved.ToDto());
    }

    public async Task<OpResult<SavedPlaceDto>> UpdateAsync(Guid principalId, Guid id, UpdateSavedPlaceRequest r, CancellationToken ct = default)
    {
        var saved = await session.LoadAsync<SavedPlace>(id, ct);
        if (saved is null || saved.PrincipalId != principalId) return OpResult<SavedPlaceDto>.NotFound();
        if (r.Label is { } label)
        {
            if (string.IsNullOrWhiteSpace(label)) return OpResult<SavedPlaceDto>.Invalid("Label cannot be blank.");
            saved.Label = label.Trim();
        }
        if (r.Icon is not null) saved.Icon = r.Icon;
        if (r.IsFavorite is { } fav) saved.IsFavorite = fav;
        session.Store(saved);
        await session.SaveChangesAsync(ct);
        return OpResult<SavedPlaceDto>.Ok(saved.ToDto());
    }

    public async Task<OpResult> DeleteAsync(Guid principalId, Guid id, CancellationToken ct = default)
    {
        var saved = await session.LoadAsync<SavedPlace>(id, ct);
        if (saved is null || saved.PrincipalId != principalId) return OpResult.NotFound();
        session.Delete(saved);
        await session.SaveChangesAsync(ct);
        return OpResult.Ok();
    }
}
