using LupiraGeoApi.Data;
using LupiraGeoApi.Domain;
using LupiraGeoApi.Dtos.AdminAreas;
using LupiraGeoApi.Mappers;
using Microsoft.EntityFrameworkCore;

namespace LupiraGeoApi.Application;

/// <summary>Browse the administrative reference tree (EF Core, <c>geo</c>): filter by level, parent, or name.</summary>
public sealed class AdminAreaService(GeoDbContext db)
{
    public async Task<OpResult<List<AdminAreaDto>>> ListAsync(AdminLevel? level, Guid? withinAreaId, string? q, int? limit, CancellationToken ct = default)
    {
        var take = Math.Clamp(limit ?? 100, 1, 500);
        IQueryable<AdminArea> query = db.AdminAreas.AsNoTracking();
        if (level is { } l) query = query.Where(a => a.Level == l);
        if (withinAreaId is { } w) query = query.Where(a => a.WithinAreaId == w);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(a => EF.Functions.ILike(a.Name, $"%{term}%"));
        }
        var rows = await query.OrderBy(a => a.Name).Take(take).ToListAsync(ct);
        return OpResult<List<AdminAreaDto>>.Ok(rows.Select(a => a.ToDto()).ToList());
    }

    public async Task<OpResult<AdminAreaDto>> GetAsync(Guid id, CancellationToken ct = default)
    {
        var area = await db.AdminAreas.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        return area is null ? OpResult<AdminAreaDto>.NotFound() : OpResult<AdminAreaDto>.Ok(area.ToDto());
    }

    /// <summary>Find-or-create the Country → Region → Locality chain from a geocode hit; returns the deepest area id.
    /// Stages inserts on the shared (scoped) change tracker — the caller commits them in its own SaveChanges.</summary>
    public async Task<Guid?> EnsureChainAsync(GeocodeHit hit, CancellationToken ct = default)
    {
        if (hit.CountryCode is null) return null;

        var country = await db.AdminAreas.FirstOrDefaultAsync(a => a.Level == AdminLevel.Country && a.IsoCode == hit.CountryCode, ct)
            ?? Add(new AdminArea { Id = Guid.NewGuid(), Level = AdminLevel.Country, Name = hit.Country ?? hit.CountryCode, IsoCode = hit.CountryCode });
        var deepest = country;

        if (hit.Region is { Length: > 0 } region)
        {
            var parentId = deepest.Id;
            deepest = await db.AdminAreas.FirstOrDefaultAsync(a => a.Level == AdminLevel.Region && a.Name == region && a.WithinAreaId == parentId, ct)
                ?? Add(new AdminArea { Id = Guid.NewGuid(), Level = AdminLevel.Region, Name = region, WithinAreaId = parentId });
        }

        if (hit.Locality is { Length: > 0 } locality)
        {
            var parentId = deepest.Id;
            deepest = await db.AdminAreas.FirstOrDefaultAsync(a => a.Level == AdminLevel.Locality && a.Name == locality && a.WithinAreaId == parentId, ct)
                ?? Add(new AdminArea { Id = Guid.NewGuid(), Level = AdminLevel.Locality, Name = locality, WithinAreaId = parentId });
        }

        return deepest.Id;

        AdminArea Add(AdminArea a) { db.AdminAreas.Add(a); return a; }
    }
}
