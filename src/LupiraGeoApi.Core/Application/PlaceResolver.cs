using LupiraGeoApi.Data;
using LupiraGeoApi.Domain;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace LupiraGeoApi.Application;

/// <summary>
/// Resolves a free-text location to a gazetteer <see cref="Place"/> — the write path that replaces LupiraCalApi's
/// global exact-string dedup. Strategy: (1) match an existing place by case-insensitive name; (2) else forward-geocode
/// and, if coordinates come back, dedupe by name+proximity or create a <see cref="PlaceSource.Geocoded"/> place with
/// coordinates and an on-demand <see cref="AdminArea"/> containment chain; (3) else provisionally create an unverified
/// <see cref="PlaceSource.User"/> place with no coordinates. The unique-ish match + upsert removes the query-then-insert
/// race the old design had.
/// </summary>
public sealed class PlaceResolver(GeoDbContext db, GeocodingService geocoder)
{
    private const double DedupeMeters = 60;

    public async Task<Place?> ResolveAsync(string? text, Guid? createdBy = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var name = string.Join(' ', text.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        // (1) Existing place by case-insensitive name.
        var existing = await db.Places.FirstOrDefaultAsync(p => EF.Functions.ILike(p.CanonicalName, name), ct);
        if (existing is not null) return existing;

        // (2) Forward-geocode. First hit with coordinates wins.
        var hit = (await geocoder.ForwardAsync(name, limit: 1, ct)).FirstOrDefault();
        if (hit is not null)
        {
            var point = new Point(hit.Lon, hit.Lat) { SRID = 4326 };

            var near = await db.Places
                .Where(p => p.Location != null && p.Location.Distance(point) <= DedupeMeters && EF.Functions.ILike(p.CanonicalName, name))
                .FirstOrDefaultAsync(ct);
            if (near is not null) return near;

            var areaId = await EnsureAreaChainAsync(hit, ct);
            var place = new Place
            {
                Id = Guid.NewGuid(),
                CanonicalName = name,
                Kind = PlaceKind.Poi,
                Category = hit.Category,
                Location = point,
                FormattedAddress = hit.DisplayName,
                WithinAreaId = areaId,
                Source = PlaceSource.Geocoded,
                Verified = false,
                CreatedByPrincipalId = createdBy,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            if (hit is { OsmType: { } t, OsmId: { } oid })
                place.ExternalIds.Add(new PlaceExternalId { Id = Guid.NewGuid(), PlaceId = place.Id, Scheme = ExternalScheme.Osm, Value = $"{t}/{oid}" });
            db.Places.Add(place);
            await db.SaveChangesAsync(ct);
            return place;
        }

        // (3) Provisional user place — no coordinates yet.
        var provisional = new Place
        {
            Id = Guid.NewGuid(),
            CanonicalName = name,
            Kind = PlaceKind.Poi,
            Category = PlaceCategory.Unknown,
            Source = PlaceSource.User,
            Verified = false,
            CreatedByPrincipalId = createdBy,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Places.Add(provisional);
        await db.SaveChangesAsync(ct);
        return provisional;
    }

    /// <summary>Find-or-create the Country → Region → Locality chain from a geocode hit; returns the deepest area id.</summary>
    private async Task<Guid?> EnsureAreaChainAsync(GeocodeHit hit, CancellationToken ct)
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
