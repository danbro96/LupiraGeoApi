using LupiraGeoApi.Core.Application.Geocoding;
using LupiraGeoApi.Core.Data;
using LupiraGeoApi.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace LupiraGeoApi.Core.Application.Places;

/// <summary>Read-only audit of merges made before the survivor's fix won: finds survivors still carrying the loser's
/// address or OSM ids. Merges never logged what they moved, so attribution is reconstructed from the loser's tombstone
/// row, its curation log, and the frozen geocode cache. Writes nothing.</summary>
public sealed class MergeOverwriteReport(GeoDbContext db, Marten.IQuerySession session)
{
    private const double SameFixDegrees = 1e-6;
    private const string OsmPrefix = nameof(ExternalScheme.Osm) + ":";

    public async Task<List<MergeOverwrite>> FindAsync(CancellationToken ct = default)
    {
        var merges = await db.CurationLog.AsNoTracking()
            .Where(e => e.Action == CurationAction.Merged && e.RelatedPlaceId != null)
            .OrderBy(e => e.Seq)
            .ToListAsync(ct);
        if (merges.Count == 0) return [];

        var places = await db.Places.AsNoTracking().ToDictionaryAsync(p => p.Id, ct);
        var osmHolders = await db.PlaceExternalIds.AsNoTracking()
            .Where(x => x.Scheme == ExternalScheme.Osm)
            .ToDictionaryAsync(x => x.Value, x => x.PlaceId, ct);
        var history = (await db.CurationLog.AsNoTracking().OrderBy(e => e.Seq).ToListAsync(ct)).ToLookup(e => e.PlaceId);
        var payloads = await Marten.QueryableExtensions.ToListAsync(
            session.Query<GeocodeCache>().Where(c => c.Kind == "forward").Select(c => c.Payload), ct);
        var cachedHits = payloads.SelectMany(GeocodingService.ParseCached)
            .Where(h => h is { OsmType: not null, OsmId: not null })
            .ToList();

        var findings = new List<MergeOverwrite>();
        foreach (var merge in merges)
        {
            if (!places.TryGetValue(merge.PlaceId, out var loser)
                || !places.TryGetValue(merge.RelatedPlaceId!.Value, out var survivor)
                || Terminal(survivor, places) is not { } target)
                continue;

            var targetEvents = history[survivor.Id].Concat(history[target.Id]).Distinct().ToList();
            // A later regeocode rewrites the address and OSM id, erasing whatever the merge brought.
            if (targetEvents.Any(e => e.Action == CurationAction.Regeocoded && e.Seq > merge.Seq)) continue;

            var inheritedAddress = !string.IsNullOrWhiteSpace(loser.FormattedAddress)
                && target.FormattedAddress == loser.FormattedAddress
                && !targetEvents.Any(e => e.Action == CurationAction.Regeocoded)
                    ? loser.FormattedAddress
                    : null;

            var addedToTarget = targetEvents
                .Where(e => e.Action == CurationAction.ExternalIdAdded && e.Detail is not null)
                .Select(e => e.Detail!)
                .ToHashSet();
            var inheritedOsm = LoserOsmIds(loser, history[loser.Id], merge.Seq, cachedHits)
                .Where(i => osmHolders.TryGetValue(i.Value, out var holder) && holder == target.Id
                    && !addedToTarget.Contains(OsmPrefix + i.Value))
                .ToList();

            if (inheritedAddress is null && inheritedOsm.Count == 0) continue;

            var osmIds = osmHolders.Where(kv => kv.Value == target.Id).Select(kv => kv.Key).Order().ToList();
            findings.Add(new MergeOverwrite(
                merge.Seq, merge.At, loser.Id, loser.CanonicalName, target.Id, target.CanonicalName,
                inheritedAddress, inheritedOsm, osmIds));
        }

        return findings;
    }

    private static IEnumerable<InheritedOsmId> LoserOsmIds(
        Place loser, IEnumerable<CurationEvent> loserEvents, long mergeSeq, List<GeocodeHit> cachedHits)
    {
        var logged = new Dictionary<string, bool>();
        foreach (var e in loserEvents.Where(e => e.Seq < mergeSeq && e.Detail?.StartsWith(OsmPrefix, StringComparison.Ordinal) == true))
        {
            if (e.Action == CurationAction.ExternalIdAdded) logged[e.Detail![OsmPrefix.Length..]] = true;
            else if (e.Action == CurationAction.ExternalIdRemoved) logged[e.Detail![OsmPrefix.Length..]] = false;
        }

        var seen = new HashSet<string>();
        foreach (var (value, present) in logged)
            if (present && seen.Add(value)) yield return new InheritedOsmId(value, InheritedOsmEvidence.LoserLog);

        foreach (var hit in cachedHits)
        {
            var atLoserFix = (loser.Location is { } loc
                    && Math.Abs(hit.Lat - loc.Y) < SameFixDegrees && Math.Abs(hit.Lon - loc.X) < SameFixDegrees)
                || (loser.FormattedAddress is { } address && hit.DisplayName == address);
            var value = $"{hit.OsmType}/{hit.OsmId}";
            if (atLoserFix && seen.Add(value)) yield return new InheritedOsmId(value, InheritedOsmEvidence.LoserGeocodeFix);
        }
    }

    private static Place? Terminal(Place place, Dictionary<Guid, Place> places)
    {
        var seen = new HashSet<Guid>();
        var cursor = place;
        while (seen.Add(cursor.Id))
        {
            if (cursor.DeletedAt is not null) return null;
            if (cursor.MergedIntoId is not { } next) return cursor;
            if (!places.TryGetValue(next, out var nextPlace)) return null;
            cursor = nextPlace;
        }

        return null;
    }
}
