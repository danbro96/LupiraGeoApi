using LupiraGeoApi.Data;
using LupiraGeoApi.Domain;
using LupiraGeoApi.Dtos.AdminAreas;
using LupiraGeoApi.Dtos.Places;
using LupiraGeoApi.Mappers;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace LupiraGeoApi.Application;

/// <summary>Read/write over the gazetteer (EF Core + PostGIS): text + spatial search, a full single place with its
/// alias/external-id/containment detail, direct create, and curation.</summary>
public sealed class PlaceQueryService(GeoDbContext db, PlaceResolver resolver)
{
    public const int MaxResults = 200;

    /// <summary>Browse/search: text (trigram), category/kind, containment, and spatial (<c>near</c> radius or <c>bbox</c>).</summary>
    public async Task<OpResult<List<PlaceDto>>> SearchAsync(
        string? q, PlaceCategory? category, PlaceKind? kind, Guid? withinAreaId,
        double? nearLat, double? nearLon, double? radiusM, double[]? bbox, int? limit, CancellationToken ct = default)
    {
        var take = Math.Clamp(limit ?? 50, 1, MaxResults);
        IQueryable<Place> query = db.Places.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(p => EF.Functions.ILike(p.CanonicalName, $"%{term}%"));
        }
        if (category is { } c) query = query.Where(p => p.Category == c);
        if (kind is { } k) query = query.Where(p => p.Kind == k);
        if (withinAreaId is { } areaId) query = query.Where(p => p.WithinAreaId == areaId);

        if (bbox is { Length: 4 })
        {
            var env = Envelope(bbox);
            query = query.Where(p => p.Location != null && p.Location.Intersects(env));
        }

        if (nearLat is { } lat && nearLon is { } lon)
        {
            var point = new Point(lon, lat) { SRID = 4326 };
            var radius = radiusM ?? 5000;
            var hits = await query
                .Where(p => p.Location != null && p.Location.Distance(point) <= radius)
                .OrderBy(p => p.Location!.Distance(point))
                .Take(take)
                .Select(p => new { Place = p, Distance = p.Location!.Distance(point) })
                .ToListAsync(ct);
            return OpResult<List<PlaceDto>>.Ok(hits.Select(h => h.Place.ToDto(h.Distance)).ToList());
        }

        var results = await query.OrderBy(p => p.CanonicalName).Take(take).ToListAsync(ct);
        return OpResult<List<PlaceDto>>.Ok(results.Select(p => p.ToDto()).ToList());
    }

    /// <summary>A single place with aliases, external ids, and the containment chain (outermost→innermost).</summary>
    public async Task<OpResult<PlaceDto>> GetAsync(Guid id, CancellationToken ct = default)
    {
        var place = await db.Places.AsNoTracking()
            .Include(p => p.Aliases).Include(p => p.ExternalIds)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (place is null) return OpResult<PlaceDto>.NotFound();

        var dto = place.ToDto();
        dto.Containment = await ContainmentAsync(place.WithinAreaId, ct);
        return OpResult<PlaceDto>.Ok(dto);
    }

    public async Task<OpResult<PlaceDto>> CreateAsync(CreatePlaceRequest r, Guid createdBy, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(r.Name)) return OpResult<PlaceDto>.Invalid("Name is required.");
        var place = new Place
        {
            Id = Guid.NewGuid(),
            CanonicalName = r.Name.Trim(),
            Kind = r.Kind,
            Category = r.Category,
            Location = r is { Latitude: { } lat, Longitude: { } lon } ? new Point(lon, lat) { SRID = 4326 } : null,
            FormattedAddress = r.FormattedAddress,
            WithinAreaId = r.WithinAreaId,
            Source = PlaceSource.User,
            Verified = false,
            CreatedByPrincipalId = createdBy,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Places.Add(place);
        await db.SaveChangesAsync(ct);
        return OpResult<PlaceDto>.Ok(place.ToDto());
    }

    public async Task<OpResult<PlaceDto>> UpdateAsync(Guid id, UpdatePlaceRequest r, CancellationToken ct = default)
    {
        var place = await db.Places.Include(p => p.Aliases).Include(p => p.ExternalIds).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (place is null) return OpResult<PlaceDto>.NotFound();
        if (r.Name is { } name)
        {
            if (string.IsNullOrWhiteSpace(name)) return OpResult<PlaceDto>.Invalid("Name cannot be blank.");
            place.CanonicalName = name.Trim();
        }
        if (r.Category is { } cat) place.Category = cat;
        if (r.Verified is { } v) place.Verified = v;
        await db.SaveChangesAsync(ct);

        var dto = place.ToDto();
        dto.Containment = await ContainmentAsync(place.WithinAreaId, ct);
        return OpResult<PlaceDto>.Ok(dto);
    }

    /// <summary>Resolve free-text to a place (match/geocode/provision) via <see cref="PlaceResolver"/>.</summary>
    public async Task<OpResult<ResolvePlaceResponse>> ResolveAsync(string text, Guid createdBy, CancellationToken ct = default)
    {
        var place = await resolver.ResolveAsync(text, createdBy, ct);
        if (place is null) return OpResult<ResolvePlaceResponse>.Invalid("Text is required.");
        return OpResult<ResolvePlaceResponse>.Ok(new ResolvePlaceResponse
        {
            PlaceId = place.Id,
            Name = place.CanonicalName,
            Latitude = place.Location?.Y,
            Longitude = place.Location?.X,
        });
    }

    private async Task<List<AdminAreaDto>> ContainmentAsync(Guid? fromAreaId, CancellationToken ct)
    {
        var chain = new List<AdminAreaDto>();
        var seen = new HashSet<Guid>();
        var cursor = fromAreaId;
        while (cursor is { } id && seen.Add(id))
        {
            var area = await db.AdminAreas.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
            if (area is null) break;
            chain.Add(area.ToDto());
            cursor = area.WithinAreaId;
        }
        chain.Reverse();
        return chain;
    }

    private static Polygon Envelope(double[] b)
    {
        // bbox = [minLon, minLat, maxLon, maxLat]
        double minLon = b[0], minLat = b[1], maxLon = b[2], maxLat = b[3];
        var ring = new LinearRing([
            new Coordinate(minLon, minLat), new Coordinate(maxLon, minLat),
            new Coordinate(maxLon, maxLat), new Coordinate(minLon, maxLat),
            new Coordinate(minLon, minLat),
        ]);
        return new Polygon(ring) { SRID = 4326 };
    }
}
