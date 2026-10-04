using Lupira.Results;
using LupiraGeoApi.Core.Application.Geocoding;
using LupiraGeoApi.Core.Data;
using LupiraGeoApi.Core.Domain;
using LupiraGeoApi.Core.Dtos.Curation;
using Microsoft.EntityFrameworkCore;

namespace LupiraGeoApi.Core.Application.Places;

/// <summary>
/// Reclassifies geocoded places created before the resolver classified hits: a place whose OSM object the frozen
/// geocode cache records as a settlement/administrative area becomes <see cref="PlaceKind.Area"/>. Offline — reads the
/// cache, never Nominatim. Only uncategorized <see cref="PlaceKind.Poi"/> places are candidates; a curated category
/// means someone already judged it a venue.
/// </summary>
public sealed class PlaceAreaSweepService(GeoDbContext db, Marten.IQuerySession session)
{
    public async Task<OpResult<List<AreaReclassificationDto>>> SweepAsync(bool apply, Guid actorId, CancellationToken ct = default)
    {
        var payloads = await Marten.QueryableExtensions.ToListAsync(session.Query<GeocodeCache>().Select(c => c.Payload), ct);
        var areaOsmIds = payloads.SelectMany(GeocodingService.ParseCached)
            .Where(h => h is { IsArea: true, OsmType: not null, OsmId: not null })
            .Select(h => $"{h.OsmType}/{h.OsmId}")
            .Distinct()
            .ToList();
        if (areaOsmIds.Count == 0) return OpResult<List<AreaReclassificationDto>>.Ok([]);

        var places = await db.Places.Include(p => p.ExternalIds)
            .Where(p => p.Kind == PlaceKind.Poi && p.Category == PlaceCategory.Unknown
                && p.DeletedAt == null && p.MergedIntoId == null
                && p.ExternalIds.Any(e => e.Scheme == ExternalScheme.Osm && areaOsmIds.Contains(e.Value)))
            .OrderBy(p => p.CanonicalName)
            .ToListAsync(ct);

        if (apply && places.Count > 0)
        {
            foreach (var place in places)
            {
                place.Kind = PlaceKind.Area;
                db.Record(place.Id, CurationAction.Reclassified, actorId, detail: nameof(PlaceKind.Area));
            }

            await db.SaveChangesAsync(ct);
        }

        return OpResult<List<AreaReclassificationDto>>.Ok([.. places.Select(p => new AreaReclassificationDto
        {
            PlaceId = p.Id,
            Name = p.CanonicalName,
            OsmId = p.ExternalIds.First(e => e.Scheme == ExternalScheme.Osm && areaOsmIds.Contains(e.Value)).Value,
            Applied = apply,
        })]);
    }
}
