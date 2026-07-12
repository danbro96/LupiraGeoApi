using System.Globalization;
using LupiraGeoApi.Data;
using LupiraGeoApi.Domain;
using LupiraGeoApi.Dtos.AdminAreas;
using LupiraGeoApi.Dtos.Places;
using LupiraGeoApi.Mappers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;

namespace LupiraGeoApi.Application;

/// <summary>Read/write over the gazetteer (EF Core + PostGIS): text + spatial search, typeahead suggest, a full single
/// place with its alias/external-id/containment detail, direct create, curation, and alias management. Reads by id
/// follow merge-tombstone redirects; every search excludes tombstones.</summary>
public sealed class PlaceQueryService(GeoDbContext db, PlaceResolver resolver, GeocodingService geocoder, AdminAreaService adminAreas, ILogger<PlaceQueryService> logger)
{
    public const int MaxResults = 200;
    public const int MaxSuggestions = 25;
    public const int MaxBatchResolve = 50;

    /// <summary>word_similarity floor — below this a trigram match is noise, not a suggestion.</summary>
    private const double SuggestMinSimilarity = 0.3;

    /// <summary>Browse/search: text (trigram), category/kind, containment, and spatial (<c>near</c> radius or <c>bbox</c>).</summary>
    public async Task<OpResult<List<PlaceDto>>> SearchAsync(
        string? q, PlaceCategory? category, PlaceKind? kind, Guid? withinAreaId,
        double? nearLat, double? nearLon, double? radiusM, double[]? bbox, int? limit, CancellationToken ct = default)
    {
        var take = Math.Clamp(limit ?? 50, 1, MaxResults);
        IQueryable<Place> query = db.Places.AsNoTracking().Where(p => p.MergedIntoId == null && p.DeletedAt == null);

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

    /// <summary>Typeahead over places (canonical name + aliases) and AdminArea localities, ranked by trigram
    /// word-similarity. Localities come from the GeoNames seed, so cities suggest without a gazetteer entry.</summary>
    public async Task<OpResult<List<PlaceSuggestionDto>>> SuggestAsync(string q, int? limit, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(q)) return OpResult<List<PlaceSuggestionDto>>.Invalid("q is required.");
        var term = q.Trim();
        var take = Math.Clamp(limit ?? 10, 1, MaxSuggestions);

        // Coordinates split client-side: ST_X/ST_Y are geometry-only, the columns are geography.
        var places = await db.Places.AsNoTracking()
            .Where(p => p.MergedIntoId == null && p.DeletedAt == null)
            .Where(p => EF.Functions.ILike(p.CanonicalName, term + "%")
                || EF.Functions.TrigramsWordSimilarity(term, p.CanonicalName) >= SuggestMinSimilarity
                || p.Aliases.Any(a => EF.Functions.ILike(a.Name, term + "%")
                    || EF.Functions.TrigramsWordSimilarity(term, a.Name) >= SuggestMinSimilarity))
            .Select(p => new
            {
                p.Id, Name = p.CanonicalName, p.Category, p.Location, p.FormattedAddress,
                Score = EF.Functions.TrigramsWordSimilarity(term, p.CanonicalName),
            })
            .OrderByDescending(x => x.Score)
            .Take(take)
            .ToListAsync(ct);

        var localities = await db.AdminAreas.AsNoTracking()
            .Where(a => a.Level == AdminLevel.Locality)
            .Where(a => EF.Functions.ILike(a.Name, term + "%")
                || EF.Functions.TrigramsWordSimilarity(term, a.Name) >= SuggestMinSimilarity)
            .Select(a => new
            {
                a.Id, a.Name, Location = a.Centroid,
                Context = a.WithinArea == null ? null : a.WithinArea.Name,
                Score = EF.Functions.TrigramsWordSimilarity(term, a.Name),
            })
            .OrderByDescending(x => x.Score)
            .Take(take)
            .ToListAsync(ct);

        var merged = places
            .Select(p => (p.Score, Dto: new PlaceSuggestionDto
            {
                Id = p.Id,
                Type = SuggestionType.Place,
                Name = p.Name,
                Category = p.Category,
                Latitude = p.Location?.Y,
                Longitude = p.Location?.X,
                Context = p.FormattedAddress,
            }))
            .Concat(localities.Select(a => (a.Score, Dto: new PlaceSuggestionDto
            {
                Id = a.Id,
                Type = SuggestionType.Locality,
                Name = a.Name,
                Latitude = a.Location?.Y,
                Longitude = a.Location?.X,
                Context = a.Context,
            })))
            .OrderByDescending(x => x.Score)
            .Take(take)
            .Select(x => x.Dto)
            .ToList();
        return OpResult<List<PlaceSuggestionDto>>.Ok(merged);
    }

    /// <summary>A single place with aliases, external ids, and the containment chain (outermost→innermost).
    /// Follows merge-tombstone redirects to the surviving place.</summary>
    public async Task<OpResult<PlaceDto>> GetAsync(Guid id, CancellationToken ct = default)
    {
        var place = await LoadCanonicalAsync(id, ct);
        if (place is null) return OpResult<PlaceDto>.NotFound();

        var dto = place.ToDto();
        dto.Containment = await ContainmentAsync(place.WithinAreaId, ct);
        return OpResult<PlaceDto>.Ok(dto);
    }

    /// <summary>Look a place up by an external gazetteer key (e.g. OSM <c>node/123</c>).</summary>
    public async Task<OpResult<PlaceDto>> GetByExternalIdAsync(ExternalScheme scheme, string value, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(value)) return OpResult<PlaceDto>.Invalid("Value is required.");
        var placeId = await db.PlaceExternalIds.AsNoTracking()
            .Where(x => x.Scheme == scheme && x.Value == value.Trim())
            .Select(x => (Guid?)x.PlaceId)
            .FirstOrDefaultAsync(ct);
        return placeId is { } pid ? await GetAsync(pid, ct) : OpResult<PlaceDto>.NotFound();
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
        db.Record(place.Id, CurationAction.Created, createdBy, detail: place.CanonicalName);
        await db.SaveChangesAsync(ct);
        return OpResult<PlaceDto>.Ok(place.ToDto());
    }

    public async Task<OpResult<PlaceDto>> UpdateAsync(Guid id, UpdatePlaceRequest r, Guid actorId, CancellationToken ct = default)
    {
        var place = await db.Places.Include(p => p.Aliases).Include(p => p.ExternalIds)
            .FirstOrDefaultAsync(p => p.Id == id && p.DeletedAt == null, ct);
        if (place is null) return OpResult<PlaceDto>.NotFound();
        if (r is { Latitude: not null, Longitude: null } or { Latitude: null, Longitude: not null })
            return OpResult<PlaceDto>.Invalid("Latitude and longitude must be supplied together.");
        if (r.Name is { } name)
        {
            if (string.IsNullOrWhiteSpace(name)) return OpResult<PlaceDto>.Invalid("Name cannot be blank.");
            name = name.Trim();
            if (!string.Equals(place.CanonicalName, name, StringComparison.Ordinal))
            {
                place.CanonicalName = name;
                db.Record(place.Id, CurationAction.Renamed, actorId, detail: name);
            }
        }
        if (r.Category is { } cat && cat != place.Category)
        {
            place.Category = cat;
            db.Record(place.Id, CurationAction.Recategorized, actorId, detail: cat.ToString());
        }
        if (r.Verified is { } v && v != place.Verified)
        {
            place.Verified = v;
            db.Record(place.Id, v ? CurationAction.Verified : CurationAction.Unverified, actorId);
        }
        if (r is { Latitude: { } lat, Longitude: { } lon })
        {
            place.Location = new Point(lon, lat) { SRID = 4326 };
            db.Record(place.Id, CurationAction.Relocated, actorId, detail: $"{lat.ToString(CultureInfo.InvariantCulture)},{lon.ToString(CultureInfo.InvariantCulture)}");
        }
        if (r.FormattedAddress is { } fa) place.FormattedAddress = fa.Trim() is { Length: > 0 } t ? t : null;
        if (r.WithinAreaId is { } areaId) place.WithinAreaId = areaId;
        await db.SaveChangesAsync(ct);

        var dto = place.ToDto();
        dto.Containment = await ContainmentAsync(place.WithinAreaId, ct);
        return OpResult<PlaceDto>.Ok(dto);
    }

    /// <summary>Re-run forward geocoding for a place from its address/name and attach the coordinates, containment
    /// chain, and OSM id — heals a coordinate-less provisional stub (or refreshes a stale fix). Leaves the place
    /// unchanged on a no-hit or a transient geocoder outage.</summary>
    public async Task<OpResult<PlaceDto>> RegeocodeAsync(Guid id, Guid actorId, CancellationToken ct = default)
    {
        var place = await db.Places.Include(p => p.ExternalIds)
            .FirstOrDefaultAsync(p => p.Id == id && p.MergedIntoId == null && p.DeletedAt == null, ct);
        if (place is null) return OpResult<PlaceDto>.NotFound();

        var query = string.IsNullOrWhiteSpace(place.FormattedAddress) ? place.CanonicalName : place.FormattedAddress!;
        var result = await geocoder.ForwardAsync(query, limit: 1, ct);
        if (result.Status == GeocodeStatus.Unavailable) return OpResult<PlaceDto>.Invalid("Geocoder unavailable; retry.");
        if (result.Hits.FirstOrDefault() is not { } hit) return OpResult<PlaceDto>.Invalid($"No geocode result for \"{query}\".");

        place.Location = new Point(hit.Lon, hit.Lat) { SRID = 4326 };
        place.FormattedAddress = hit.DisplayName;
        place.WithinAreaId = await adminAreas.EnsureChainAsync(hit, ct);
        place.Source = PlaceSource.Geocoded;

        foreach (var old in place.ExternalIds.Where(x => x.Scheme == ExternalScheme.Osm).ToList())
        {
            place.ExternalIds.Remove(old);
            db.PlaceExternalIds.Remove(old);
        }
        if (hit is { OsmType: { } t, OsmId: { } oid })
        {
            var ext = new PlaceExternalId { Id = Guid.NewGuid(), PlaceId = place.Id, Scheme = ExternalScheme.Osm, Value = $"{t}/{oid}" };
            place.ExternalIds.Add(ext);
            db.PlaceExternalIds.Add(ext);
        }
        db.Record(place.Id, CurationAction.Regeocoded, actorId, detail: hit.DisplayName);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // Regeocode attaches the hit's OSM id; if another place already claims it this violates the unique
            // (Scheme, Value) index. Log it (the MCP layer would otherwise hide it) and fail cleanly.
            logger.LogError(ex, "Regeocoding place {PlaceId} failed to persist; OSM id {Osm} may already belong to another place.",
                id, hit is { OsmType: { } ot, OsmId: { } oi } ? $"{ot}/{oi}" : "(none)");
            return OpResult<PlaceDto>.Conflict("Could not persist the regeocode; its OSM id may already belong to another place.");
        }

        var dto = place.ToDto();
        dto.Containment = await ContainmentAsync(place.WithinAreaId, ct);
        return OpResult<PlaceDto>.Ok(dto);
    }

    /// <summary>Soft-delete a place: a bad entry (e.g. a wrong geocode) with no valid survivor to merge into. Tombstoned
    /// (<see cref="Place.DeletedAt"/>), so reads 404 and search/resolve exclude it; the row stays for the audit trail.
    /// Idempotent.</summary>
    public async Task<OpResult> DeleteAsync(Guid id, Guid actorId, CancellationToken ct = default)
    {
        var place = await db.Places.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (place is null) return OpResult.NotFound();
        if (place.DeletedAt is not null) return OpResult.Ok();
        place.DeletedAt = DateTimeOffset.UtcNow;
        db.Record(place.Id, CurationAction.Deleted, actorId);
        await db.SaveChangesAsync(ct);
        return OpResult.Ok();
    }

    public async Task<OpResult<PlaceDto>> AddAliasAsync(Guid placeId, AddAliasRequest r, Guid actorId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(r.Name)) return OpResult<PlaceDto>.Invalid("Name is required.");
        var name = r.Name.Trim();

        var place = await db.Places.Include(p => p.Aliases).Include(p => p.ExternalIds)
            .FirstOrDefaultAsync(p => p.Id == placeId, ct);
        if (place is null) return OpResult<PlaceDto>.NotFound();
        if (string.Equals(place.CanonicalName, name, StringComparison.OrdinalIgnoreCase))
            return OpResult<PlaceDto>.Conflict("Alias equals the canonical name.");
        if (place.Aliases.Any(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)))
            return OpResult<PlaceDto>.Conflict("Alias already exists.");

        // Add via the set — a nav-discovered entity with a pre-set Guid key would be treated as an update.
        // Change-tracker fixup puts it into place.Aliases for the DTO below.
        db.PlaceAliases.Add(new PlaceAlias
        {
            Id = Guid.NewGuid(),
            PlaceId = place.Id,
            Name = name,
            Lang = string.IsNullOrWhiteSpace(r.Lang) ? null : r.Lang.Trim(),
        });
        db.Record(place.Id, CurationAction.AliasAdded, actorId, detail: name);
        await db.SaveChangesAsync(ct);

        var dto = place.ToDto();
        dto.Containment = await ContainmentAsync(place.WithinAreaId, ct);
        return OpResult<PlaceDto>.Ok(dto);
    }

    public async Task<OpResult> RemoveAliasAsync(Guid placeId, Guid aliasId, Guid actorId, CancellationToken ct = default)
    {
        var alias = await db.PlaceAliases.FirstOrDefaultAsync(a => a.Id == aliasId && a.PlaceId == placeId, ct);
        if (alias is null) return OpResult.NotFound();
        db.PlaceAliases.Remove(alias);
        db.Record(placeId, CurationAction.AliasRemoved, actorId, detail: alias.Name);
        await db.SaveChangesAsync(ct);
        return OpResult.Ok();
    }

    /// <summary>Resolve free-text to a place (match/geocode/provision) via <see cref="PlaceResolver"/>. A transient
    /// geocoder outage returns <see cref="PlaceResolution.GeocodeUnavailable"/> with a null <c>PlaceId</c> (nothing
    /// created) — an Ok result so a batch does not abort; the caller retries that item.</summary>
    public async Task<OpResult<ResolvePlaceResponse>> ResolveAsync(string text, Guid createdBy, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return OpResult<ResolvePlaceResponse>.Invalid("Text is required.");
        ResolveOutcome outcome;
        try
        {
            outcome = await resolver.ResolveAsync(text, createdBy, ct);
        }
        catch (DbUpdateException ex)
        {
            // Otherwise the MCP layer swallows this into an opaque "An error occurred invoking …". A concurrent
            // resolve of the same geocoded OSM object can still race the dedup and collide on the unique external id.
            logger.LogError(ex, "Resolving {Text} failed to persist; a conflicting gazetteer entry may already exist.", text);
            return OpResult<ResolvePlaceResponse>.Conflict("Could not persist the resolved place; a conflicting gazetteer entry may already exist.");
        }
        return OpResult<ResolvePlaceResponse>.Ok(new ResolvePlaceResponse
        {
            Resolution = outcome.Resolution,
            PlaceId = outcome.Place?.Id,
            Name = outcome.Place?.CanonicalName ?? text.Trim(),
            Latitude = outcome.Place?.Location?.Y,
            Longitude = outcome.Place?.Location?.X,
        });
    }

    /// <summary>Bulk resolve; responses align index-for-index with the input. All-or-nothing on invalid items.</summary>
    public async Task<OpResult<List<ResolvePlaceResponse>>> ResolveBatchAsync(
        List<string> texts, Guid createdBy, CancellationToken ct = default)
    {
        if (texts is not { Count: > 0 }) return OpResult<List<ResolvePlaceResponse>>.Invalid("Texts is required.");
        if (texts.Count > MaxBatchResolve)
            return OpResult<List<ResolvePlaceResponse>>.Invalid($"At most {MaxBatchResolve} texts per batch.");

        var responses = new List<ResolvePlaceResponse>(texts.Count);
        foreach (var text in texts)
        {
            var r = await ResolveAsync(text, createdBy, ct);
            if (!r.IsOk) return OpResult<List<ResolvePlaceResponse>>.Invalid($"Item {responses.Count}: {r.Error}");
            responses.Add(r.Value!);
        }
        return OpResult<List<ResolvePlaceResponse>>.Ok(responses);
    }

    /// <summary>Load a place by id, following the merge-tombstone chain to the survivor (cycle-guarded).</summary>
    private async Task<Place?> LoadCanonicalAsync(Guid id, CancellationToken ct)
    {
        var seen = new HashSet<Guid>();
        Guid? cursor = id;
        while (cursor is { } cid && seen.Add(cid))
        {
            var place = await db.Places.AsNoTracking()
                .Include(p => p.Aliases).Include(p => p.ExternalIds)
                .FirstOrDefaultAsync(p => p.Id == cid, ct);
            if (place is null) return null;
            if (place.DeletedAt is not null) return null; // soft-deleted: no redirect, reads 404
            if (place.MergedIntoId is null) return place;
            cursor = place.MergedIntoId;
        }
        return null;
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
