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
}
