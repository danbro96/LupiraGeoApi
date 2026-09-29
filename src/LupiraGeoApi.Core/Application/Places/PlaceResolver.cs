using LupiraGeoApi.Core.Application.Geocoding;
using LupiraGeoApi.Core.Data;
using LupiraGeoApi.Core.Domain;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace LupiraGeoApi.Core.Application.Places;

/// <summary>
/// Resolves a free-text location to a gazetteer <see cref="Place"/> — the write path that replaces LupiraCalApi's
/// global exact-string dedup. Strategy: (1) match an existing place by normalized name or alias; (2) else forward-geocode
/// and, if coordinates come back, dedupe by name+proximity or create a <see cref="PlaceSource.Geocoded"/> place with
/// coordinates and an on-demand <see cref="AdminArea"/> containment chain; (3) on a definitive empty result provisionally
/// create an unverified <see cref="PlaceSource.User"/> place with no coordinates. A transient geocoder outage
/// (<see cref="GeocodeStatus.Unavailable"/>) creates NOTHING — it returns <see cref="PlaceResolution.GeocodeUnavailable"/>
/// so a retry can succeed later, instead of poisoning the gazetteer with an unhealable stub. The caller must pass
/// non-blank text (validated at the service boundary).
/// </summary>
public sealed class PlaceResolver(GeoDbContext db, GeocodingService geocoder, AdminAreaService adminAreas)
{
    private const double DedupeMeters = 60;

    /// <summary>word_similarity floor for the proximity dedup — within 60 m, this much name overlap is the same thing.</summary>
    private const double DedupeSimilarity = 0.7;

    public async Task<ResolveOutcome> ResolveAsync(string text, Guid? createdBy = null, CancellationToken ct = default)
    {
        var name = PlaceTextNormalizer.Canonical(text);
        var key = PlaceTextNormalizer.Key(name);

        // (1) Existing place by normalized name or alias. Ordered so a healed place beats a stub that folds the same
        // way — otherwise which duplicate wins depends on Postgres' row order.
        var existing = await db.Places
            .Where(p => p.MergedIntoId == null && p.DeletedAt == null
                && (p.NormalizedName == key || p.Aliases.Any(a => a.NormalizedName == key)))
            .OrderByDescending(p => p.Location != null)
            .ThenByDescending(p => p.Verified)
            .ThenBy(p => p.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (existing is not null) return new ResolveOutcome(existing, PlaceResolution.Matched);

        // (2) Forward-geocode. A transient outage stops here — do not create anything.
        var result = await geocoder.ForwardAsync(name, limit: 1, ct: ct);
        if (result.Status == GeocodeStatus.Unavailable) return new ResolveOutcome(null, PlaceResolution.GeocodeUnavailable);

        if (result.Hits.FirstOrDefault() is { } hit)
            return await ResolveFromHitAsync(name, hit, createdBy, ct);

        // (3) Definitive no-hit → provisional user place with no coordinates yet.
        var provisional = new Place
        {
            Id = Guid.NewGuid(),
            CanonicalName = name,
            NormalizedName = key,
            Kind = PlaceKind.Poi,
            Category = PlaceCategory.Unknown,
            Source = PlaceSource.User,
            Verified = false,
            CreatedByPrincipalId = createdBy,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Places.Add(provisional);
        db.Record(provisional.Id, CurationAction.Created, createdBy, detail: provisional.CanonicalName);
        await db.SaveChangesAsync(ct);
        return new ResolveOutcome(provisional, PlaceResolution.Provisional);
    }

    /// <summary>Land one specific geocode hit as a place: dedupe by OSM identity, then by name+proximity, else create
    /// a <see cref="PlaceSource.Geocoded"/> place with coordinates and containment. Shared by text resolve (first hit)
    /// and create-from-geocode (user-picked hit).</summary>
    internal async Task<ResolveOutcome> ResolveFromHitAsync(string name, GeocodeHit hit, Guid? createdBy, CancellationToken ct)
    {
        var point = new Point(hit.Lon, hit.Lat) { SRID = 4326 };
        var key = PlaceTextNormalizer.Key(name);
        var osmId = hit is { OsmType: { } t, OsmId: { } oid } ? $"{t}/{oid}" : null;

        // Dedup by OSM identity first: one real-world object resolves here under many text forms (a bare name
        // vs a comma-qualified address), and name+proximity dedup misses them because the canonical name differs.
        // Without this the second resolve inserts a duplicate (Scheme, Value) and SaveChanges throws on the unique index.
        if (osmId is not null)
        {
            var byOsm = await db.Places.FirstOrDefaultAsync(
                p => p.MergedIntoId == null && p.DeletedAt == null
                && p.ExternalIds.Any(x => x.Scheme == ExternalScheme.Osm && x.Value == osmId), ct);
            if (byOsm is not null) return new ResolveOutcome(byOsm, PlaceResolution.Matched);
        }

        // Similarity, not equality: "Bara enkelt" and "Bara enkelt, Skånegatan 59" are one venue. word_similarity is
        // asymmetric (it scores the best extent of the second argument), so try both directions.
        var near = await db.Places
            .Where(p => p.MergedIntoId == null && p.DeletedAt == null && p.Location != null
                && p.Location.Distance(point) <= DedupeMeters
                && (p.NormalizedName == key
                    || EF.Functions.TrigramsWordSimilarity(key, p.NormalizedName) >= DedupeSimilarity
                    || EF.Functions.TrigramsWordSimilarity(p.NormalizedName, key) >= DedupeSimilarity))
            .FirstOrDefaultAsync(ct);
        if (near is not null) return new ResolveOutcome(near, PlaceResolution.Matched);

        var areaId = await adminAreas.EnsureChainAsync(hit, ct);
        var place = new Place
        {
            Id = Guid.NewGuid(),
            CanonicalName = name,
            NormalizedName = key,
            Kind = hit.IsArea ? PlaceKind.Area : PlaceKind.Poi,
            Category = hit.Category,
            Location = point,
            FormattedAddress = hit.DisplayName,
            WithinAreaId = areaId,
            Source = PlaceSource.Geocoded,
            Verified = false,
            CreatedByPrincipalId = createdBy,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        if (osmId is not null)
            place.ExternalIds.Add(new PlaceExternalId { Id = Guid.NewGuid(), PlaceId = place.Id, Scheme = ExternalScheme.Osm, Value = osmId });
        db.Places.Add(place);
        db.Record(place.Id, CurationAction.Created, createdBy, detail: place.CanonicalName);
        await db.SaveChangesAsync(ct);
        return new ResolveOutcome(place, PlaceResolution.Geocoded);
    }
}
