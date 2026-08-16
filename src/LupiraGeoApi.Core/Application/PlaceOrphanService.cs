using LupiraGeoApi.Data;
using LupiraGeoApi.Domain;
using LupiraGeoApi.Dtos.Curation;
using Marten;
using Microsoft.EntityFrameworkCore;

namespace LupiraGeoApi.Application;

/// <summary>
/// Cross-service orphan detection and pruning. References live in LupiraContactApi (contact addresses), LupiraCalApi
/// (item locations + travel legs), and locally in SavedPlaces; the check endpoints answer counts for candidate ids.
/// FAIL CLOSED: if either external source is unconfigured or unreachable, no orphan is declared — partial reference
/// data must never read as "unreferenced". The check request includes merge tombstones (a consumer may still hold a
/// loser id) and returned counts are canonicalized through the merge chain onto the survivor. Prune re-gathers
/// references fresh, so find-vs-prune races resolve to <see cref="PruneStatus.Referenced"/>, and is a soft delete
/// (the manual override stays <c>DELETE /places/{id}</c>).
/// </summary>
public sealed class PlaceOrphanService(
    GeoDbContext db, IDocumentSession session,
    IContactPlaceReferences contactRefs, ICalendarPlaceReferences calendarRefs)
{
    public const int MaxPruneBatch = 100;

    private sealed record RefCounts(int Contact, int CalendarLive, int CalendarDeleted, int Saved)
    {
        public static readonly RefCounts Zero = new(0, 0, 0, 0);
        public bool Unreferenced => Contact == 0 && CalendarLive == 0 && Saved == 0;
        public bool Prunable => Unreferenced && CalendarDeleted == 0;
    }

    public async Task<OpResult<List<OrphanCandidateDto>>> FindOrphansAsync(CancellationToken ct = default)
    {
        var gathered = await GatherReferencesAsync(ct);
        if (gathered is not { } refs)
            return OpResult<List<OrphanCandidateDto>>.Invalid(
                "Contact/calendar reference source unavailable — orphan sweep needs complete reference data; retry.");

        var live = await EntityFrameworkQueryableExtensions.ToListAsync(
            db.Places.AsNoTracking().Where(p => p.MergedIntoId == null && p.DeletedAt == null), ct);

        var candidates = live
            .Select(p => (Place: p, Counts: refs.GetValueOrDefault(p.Id, RefCounts.Zero)))
            .Where(x => x.Counts.Unreferenced)
            .OrderBy(x => x.Place.CreatedAt)
            .Select(x => new OrphanCandidateDto
            {
                PlaceId = x.Place.Id,
                Name = x.Place.CanonicalName,
                Kind = x.Place.Kind,
                Category = x.Place.Category,
                Source = x.Place.Source,
                Verified = x.Place.Verified,
                HasCoordinates = x.Place.Location != null,
                CreatedAt = x.Place.CreatedAt,
                ContactRefs = x.Counts.Contact,
                CalendarLiveRefs = x.Counts.CalendarLive,
                CalendarDeletedRefs = x.Counts.CalendarDeleted,
                SavedPlaceRefs = x.Counts.Saved,
                Prunable = x.Counts.Prunable,
            })
            .ToList();
        return OpResult<List<OrphanCandidateDto>>.Ok(candidates);
    }

    public async Task<OpResult<List<PrunePlaceResultDto>>> PruneAsync(List<Guid> placeIds, Guid actorId, CancellationToken ct = default)
    {
        if (placeIds.Count == 0) return OpResult<List<PrunePlaceResultDto>>.Invalid("PlaceIds is required.");
        if (placeIds.Count > MaxPruneBatch) return OpResult<List<PrunePlaceResultDto>>.Invalid($"At most {MaxPruneBatch} ids per prune.");

        // Fresh gather — the concurrent-resolve guard: a place referenced since the find lands as Referenced.
        var gathered = await GatherReferencesAsync(ct);
        if (gathered is not { } refs)
            return OpResult<List<PrunePlaceResultDto>>.Invalid(
                "Contact/calendar reference source unavailable — orphan sweep needs complete reference data; retry.");

        var places = await db.Places
            .Where(p => placeIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        var results = new List<PrunePlaceResultDto>(placeIds.Count);
        foreach (var id in placeIds.Distinct())
        {
            if (!places.TryGetValue(id, out var place) || place.MergedIntoId != null)
            {
                results.Add(new PrunePlaceResultDto { PlaceId = id, Status = PruneStatus.NotFound });
                continue;
            }

            if (place.DeletedAt != null)
            {
                results.Add(new PrunePlaceResultDto { PlaceId = id, Status = PruneStatus.Pruned, Reason = "Already deleted." });
                continue;
            }

            var counts = refs.GetValueOrDefault(id, RefCounts.Zero);
            if (!counts.Prunable)
            {
                results.Add(new PrunePlaceResultDto
                {
                    PlaceId = id,
                    Status = PruneStatus.Referenced,
                    Reason = counts.Unreferenced
                        ? "Referenced only by soft-deleted calendar items — an undelete would dangle; delete manually if intended."
                        : $"Still referenced (contacts: {counts.Contact}, calendar: {counts.CalendarLive}, saved: {counts.Saved}).",
                });
                continue;
            }

            place.DeletedAt = DateTimeOffset.UtcNow;
            db.Record(place.Id, CurationAction.Deleted, actorId, detail: "orphan-prune");
            results.Add(new PrunePlaceResultDto { PlaceId = id, Status = PruneStatus.Pruned });
        }

        await db.SaveChangesAsync(ct);
        return OpResult<List<PrunePlaceResultDto>>.Ok(results);
    }

    /// <summary>All reference counts keyed by canonical (survivor) place id, or null when a source is unavailable.
    /// Sends live AND merge-tombstone ids to the check endpoints; counts on loser ids accrue to the survivor.</summary>
    private async Task<Dictionary<Guid, RefCounts>?> GatherReferencesAsync(CancellationToken ct)
    {
        if (!contactRefs.IsConfigured || !calendarRefs.IsConfigured) return null;

        var chains = await db.Places.AsNoTracking()
            .Where(p => p.DeletedAt == null || p.MergedIntoId != null)
            .Select(p => new { p.Id, p.MergedIntoId })
            .ToDictionaryAsync(p => p.Id, p => p.MergedIntoId, ct);
        var allIds = chains.Keys.ToList();

        var contact = await contactRefs.CheckAsync(allIds, ct);
        if (contact is null) return null;
        var calendar = await calendarRefs.CheckAsync(allIds, ct);
        if (calendar is null) return null;

        var counts = new Dictionary<Guid, RefCounts>();
        void Accrue(Guid id, int c, int cl, int cd, int s)
        {
            if (Canonical(id) is not { } target) return;   // dangling ref (unknown/deleted id) — nothing to protect
            var cur = counts.GetValueOrDefault(target, RefCounts.Zero);
            counts[target] = new RefCounts(cur.Contact + c, cur.CalendarLive + cl, cur.CalendarDeleted + cd, cur.Saved + s);
        }

        foreach (var r in contact) Accrue(r.PlaceId, r.Count, 0, 0, 0);
        foreach (var r in calendar) Accrue(r.PlaceId, 0, r.LiveCount, r.DeletedCount, 0);

        var saved = await Marten.QueryableExtensions.ToListAsync(
            session.Query<SavedPlace>().Where(s => s.PlaceId != null), ct);
        foreach (var s in saved) Accrue(s.PlaceId!.Value, 0, 0, 0, 1);

        return counts;

        Guid? Canonical(Guid id)
        {
            var seen = new HashSet<Guid>();
            var current = id;
            while (seen.Add(current))
            {
                if (!chains.TryGetValue(current, out var mergedInto)) return null;
                if (mergedInto is not { } next) return current;
                current = next;
            }

            return null;   // cycle — defensive, merge writes guard against this
        }
    }
}
