using LupiraGeoApi.Data;
using LupiraGeoApi.Domain;
using LupiraGeoApi.Dtos.SavedPlaces;
using LupiraGeoApi.Mappers;
using Marten;
using Microsoft.EntityFrameworkCore;
using JasperFx; // ConcurrencyException moved here in Marten 9 (JasperFx core).

namespace LupiraGeoApi.Application;

/// <summary>Per-principal saved places / personal labels (Marten, <c>geo_user</c>). Every operation is scoped to the
/// calling principal; a saved place owned by someone else reads as <c>NotFound</c>. A saved place either links a
/// gazetteer place (its coordinate resolves from the <c>geo</c> gazetteer on read) or carries a raw coordinate.</summary>
public sealed class SavedPlaceService(IDocumentSession session, GeoDbContext db)
{
    public async Task<OpResult<List<SavedPlaceDto>>> ListAsync(Guid principalId, CancellationToken ct = default)
    {
        var saved = await Marten.QueryableExtensions.ToListAsync(
            session.Query<SavedPlace>()
                .Where(s => s.PrincipalId == principalId)
                .OrderByDescending(s => s.IsFavorite).ThenBy(s => s.Label), ct);
        return OpResult<List<SavedPlaceDto>>.Ok(await ToDtosAsync(saved, ct));
    }

    public async Task<OpResult<SavedPlaceDto>> CreateAsync(Guid principalId, CreateSavedPlaceRequest r, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(r.Label)) return OpResult<SavedPlaceDto>.Invalid("Label is required.");
        if (r.PlaceId is null && r is not { Latitude: not null, Longitude: not null })
            return OpResult<SavedPlaceDto>.Invalid("A saved place needs either a placeId or a latitude/longitude.");

        var now = DateTimeOffset.UtcNow;
        var saved = new SavedPlace
        {
            Id = Guid.NewGuid(),
            PrincipalId = principalId,
            PlaceId = r.PlaceId,
            RawLat = r.Latitude,
            RawLon = r.Longitude,
            Label = r.Label.Trim(),
            Icon = r.Icon,
            Notes = string.IsNullOrWhiteSpace(r.Notes) ? null : r.Notes.Trim(),
            IsFavorite = r.IsFavorite,
            CreatedAt = now,
            UpdatedAt = now,
        };
        session.Store(saved);
        await session.SaveChangesAsync(ct);
        return OpResult<SavedPlaceDto>.Ok(await ToDtoAsync(saved, ct));
    }

    public async Task<OpResult<SavedPlaceDto>> UpdateAsync(Guid principalId, Guid id, UpdateSavedPlaceRequest r, CancellationToken ct = default)
    {
        var saved = await session.LoadAsync<SavedPlace>(id, ct);
        if (saved is null || saved.PrincipalId != principalId) return OpResult<SavedPlaceDto>.NotFound();

        // Re-point: placeId XOR raw coordinates. Omitting all three leaves the target untouched; a saved place always
        // has a target (matches CreateAsync), so a link is dropped by re-pointing to a raw coordinate, not nulled.
        if (r.PlaceId is not null && (r.Latitude is not null || r.Longitude is not null))
            return OpResult<SavedPlaceDto>.Invalid("Provide either placeId or latitude/longitude, not both.");
        if (r is { Latitude: not null, Longitude: null } or { Latitude: null, Longitude: not null })
            return OpResult<SavedPlaceDto>.Invalid("Latitude and longitude must be supplied together.");
        if (r.PlaceId is { } pid) { saved.PlaceId = pid; saved.RawLat = null; saved.RawLon = null; }
        else if (r is { Latitude: { } lat, Longitude: { } lon }) { saved.RawLat = lat; saved.RawLon = lon; saved.PlaceId = null; }

        if (r.Label is { } label)
        {
            if (string.IsNullOrWhiteSpace(label)) return OpResult<SavedPlaceDto>.Invalid("Label cannot be blank.");
            saved.Label = label.Trim();
        }
        if (r.Icon is not null) saved.Icon = r.Icon;
        if (r.Notes is not null) saved.Notes = string.IsNullOrWhiteSpace(r.Notes) ? null : r.Notes.Trim();
        if (r.IsFavorite is { } fav) saved.IsFavorite = fav;
        saved.UpdatedAt = DateTimeOffset.UtcNow;
        session.Store(saved);
        // Optimistic concurrency: another device modifying this doc between our load and save throws.
        try { await session.SaveChangesAsync(ct); }
        catch (ConcurrencyException) { return OpResult<SavedPlaceDto>.Conflict("Saved place was modified concurrently; reload and retry."); }
        return OpResult<SavedPlaceDto>.Ok(await ToDtoAsync(saved, ct));
    }

    public async Task<OpResult> DeleteAsync(Guid principalId, Guid id, CancellationToken ct = default)
    {
        var saved = await session.LoadAsync<SavedPlace>(id, ct);
        if (saved is null || saved.PrincipalId != principalId) return OpResult.NotFound();
        session.Delete(saved);
        await session.SaveChangesAsync(ct);
        return OpResult.Ok();
    }

    private async Task<SavedPlaceDto> ToDtoAsync(SavedPlace s, CancellationToken ct) => (await ToDtosAsync([s], ct))[0];

    /// <summary>Map to DTOs, resolving the effective coordinate: raw when raw-backed, else the linked gazetteer place's
    /// point (batched, one query). <c>PlaceId</c> still distinguishes linked vs raw. A deleted/coordinate-less linked
    /// place yields null coordinates.</summary>
    private async Task<List<SavedPlaceDto>> ToDtosAsync(IReadOnlyList<SavedPlace> rows, CancellationToken ct)
    {
        var ids = rows.Where(s => s.PlaceId is not null && s.RawLat is null)
            .Select(s => s.PlaceId!.Value).Distinct().ToList();
        var coords = new Dictionary<Guid, (double Lat, double Lon)>();
        if (ids.Count > 0)
        {
            // Project the Point and split .Y/.X in memory — ST_X/ST_Y are geometry-only on a geography column.
            var pts = await EntityFrameworkQueryableExtensions.ToListAsync(
                db.Places.AsNoTracking()
                    .Where(p => ids.Contains(p.Id) && p.Location != null && p.DeletedAt == null)
                    .Select(p => new { p.Id, p.Location }), ct);
            foreach (var p in pts) coords[p.Id] = (p.Location!.Y, p.Location.X);
        }
        return rows.Select(s =>
        {
            var dto = s.ToDto();
            if (dto.Latitude is null && s.PlaceId is { } pid && coords.TryGetValue(pid, out var c))
                (dto.Latitude, dto.Longitude) = (c.Lat, c.Lon);
            return dto;
        }).ToList();
    }
}
