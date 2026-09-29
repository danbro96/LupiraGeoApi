using System.Globalization;
using LupiraGeoApi.Core.Application.Geocoding;
using LupiraGeoApi.Core.Application.Results;
using LupiraGeoApi.Core.Data;
using LupiraGeoApi.Core.Domain;
using LupiraGeoApi.Core.Dtos.AdminAreas;
using LupiraGeoApi.Core.Dtos.Places;
using LupiraGeoApi.Core.Mappers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;

namespace LupiraGeoApi.Core.Application.Places;

/// <summary>Read/write over the gazetteer (EF Core + PostGIS): text + spatial search, typeahead suggest, a full single
/// place with its alias/external-id/containment detail, direct create, curation, and alias management. Reads by id
/// follow merge-tombstone redirects; every search excludes tombstones.</summary>
public sealed class PlaceQueryService(GeoDbContext db, PlaceResolver resolver, GeocodingService geocoder, AdminAreaService adminAreas, ILogger<PlaceQueryService> logger)
{
    public const int MaxResults = 200;
    public const int MaxSuggestions = 25;
    public const int MaxBatchResolve = 50;
    public const int MaxBatchRegeocode = 50;

    /// <summary>word_similarity floor — below this a trigram match is noise, not a suggestion.</summary>
    private const double SuggestMinSimilarity = 0.3;

    /// <summary>Browse/search: text (trigram), category/kind, containment, curation state (<c>hasCoordinates</c>/
    /// <c>source</c>/<c>verified</c>), and spatial (<c>near</c> radius or <c>bbox</c>).</summary>
    public async Task<OpResult<List<PlaceDto>>> SearchAsync(
        string? q, PlaceCategory? category, PlaceKind? kind, Guid? withinAreaId,
        bool? hasCoordinates, PlaceSource? source, bool? verified,
        double? nearLat, double? nearLon, double? radiusM, double[]? bbox, int? limit, CancellationToken ct = default)
    {
        var take = Math.Clamp(limit ?? 50, 1, MaxResults);
        var query = db.Places.AsNoTracking().Where(p => p.MergedIntoId == null && p.DeletedAt == null);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(p => EF.Functions.ILike(p.CanonicalName, $"%{term}%"));
        }

        if (category is { } c) query = query.Where(p => p.Category == c);
        if (kind is { } k) query = query.Where(p => p.Kind == k);
        if (withinAreaId is { } areaId) query = query.Where(p => p.WithinAreaId == areaId);
        if (hasCoordinates is { } hc) query = query.Where(p => (p.Location != null) == hc);
        if (source is { } src) query = query.Where(p => p.Source == src);
        if (verified is { } v) query = query.Where(p => p.Verified == v);

        if (bbox is { Length: > 0 and not 4 })
            return OpResult<List<PlaceDto>>.Invalid("bbox requires exactly 4 values: minLon, minLat, maxLon, maxLat.");

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

    /// <summary>Typeahead over places (canonical name + aliases) and AdminArea localities: prefix matches first,
    /// then trigram word-similarity scored against the best of name/alias. Localities come from the GeoNames seed,
    /// so cities suggest without a gazetteer entry; a same-name place shadows its locality.</summary>
    public async Task<OpResult<List<PlaceSuggestionDto>>> SuggestAsync(string q, int? limit, CancellationToken ct = default)
    {
        var term = q?.Trim() ?? string.Empty;
        if (term.Length < 2) return OpResult<List<PlaceSuggestionDto>>.Invalid("q must be at least 2 characters.");
        var take = Math.Clamp(limit ?? 10, 1, MaxSuggestions);
        var prefix = EscapeLike(term) + "%";

        // <% is served by the GIN trigram indexes (the >= function form is not); the similarity in the projection
        // only scores the index-filtered survivors. The operator reads pg_trgm.word_similarity_threshold — set per
        // transaction so the floor stays a code-owned constant without role/database config.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync(
            string.Create(CultureInfo.InvariantCulture, $"SET LOCAL pg_trgm.word_similarity_threshold = {SuggestMinSimilarity}"), ct);

        // Coordinates split client-side: ST_X/ST_Y are geometry-only, the columns are geography.
        var places = await db.Places.AsNoTracking()
            .Where(p => p.MergedIntoId == null && p.DeletedAt == null)
            .Where(p => EF.Functions.ILike(p.CanonicalName, prefix)
                || EF.Functions.TrigramsAreWordSimilar(term, p.CanonicalName)
                || p.Aliases.Any(a => EF.Functions.ILike(a.Name, prefix)
                    || EF.Functions.TrigramsAreWordSimilar(term, a.Name)))
            .Select(p => new
            {
                p.Id,
                Name = p.CanonicalName,
                p.Category,
                p.Location,
                p.FormattedAddress,
                IsPrefix = EF.Functions.ILike(p.CanonicalName, prefix) || p.Aliases.Any(a => EF.Functions.ILike(a.Name, prefix)),
                Score = Math.Max(
                    EF.Functions.TrigramsWordSimilarity(term, p.CanonicalName),
                    p.Aliases.Select(a => (double?) EF.Functions.TrigramsWordSimilarity(term, a.Name)).Max() ?? 0),
            })
            .OrderByDescending(x => x.IsPrefix).ThenByDescending(x => x.Score)
            .Take(take)
            .ToListAsync(ct);

        var localities = await db.AdminAreas.AsNoTracking()
            .Where(a => a.Level == AdminLevel.Locality)
            .Where(a => EF.Functions.ILike(a.Name, prefix) || EF.Functions.TrigramsAreWordSimilar(term, a.Name))
            .Select(a => new
            {
                a.Id,
                a.Name,
                Location = a.Centroid,
                Context = a.WithinArea == null ? null : a.WithinArea.Name,
                IsPrefix = EF.Functions.ILike(a.Name, prefix),
                Score = EF.Functions.TrigramsWordSimilarity(term, a.Name),
            })
            .OrderByDescending(x => x.IsPrefix).ThenByDescending(x => x.Score)
            .Take(take)
            .ToListAsync(ct);

        await tx.CommitAsync(ct);

        var placeNames = places.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var merged = places
            .Select(p => (p.IsPrefix, p.Score, Dto: new PlaceSuggestionDto
            {
                Id = p.Id,
                Type = SuggestionType.Place,
                Name = p.Name,
                Category = p.Category,
                Latitude = p.Location?.Y,
                Longitude = p.Location?.X,
                Context = p.FormattedAddress,
            }))
            .Concat(localities
                .Where(a => !placeNames.Contains(a.Name.Trim()))
                .Select(a => (a.IsPrefix, a.Score, Dto: new PlaceSuggestionDto
                {
                    Id = a.Id,
                    Type = SuggestionType.Locality,
                    Name = a.Name,
                    Latitude = a.Location?.Y,
                    Longitude = a.Location?.X,
                    Context = a.Context,
                })))
            .OrderByDescending(x => x.IsPrefix).ThenByDescending(x => x.Score)
            .Take(take)
            .Select(x => x.Dto)
            .ToList();
        return OpResult<List<PlaceSuggestionDto>>.Ok(merged);
    }

    private static string EscapeLike(string s) => s.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");

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
            .Select(x => (Guid?) x.PlaceId)
            .FirstOrDefaultAsync(ct);
        return placeId is { } pid ? await GetAsync(pid, ct) : OpResult<PlaceDto>.NotFound();
    }

    public async Task<OpResult<PlaceDto>> CreateAsync(CreatePlaceRequest r, Guid createdBy, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(r.Name)) return OpResult<PlaceDto>.Invalid("Name is required.");
        var name = PlaceTextNormalizer.Canonical(r.Name);
        var key = PlaceTextNormalizer.Key(name);

        var clash = await db.Places.AsNoTracking()
            .FirstOrDefaultAsync(p => p.MergedIntoId == null && p.DeletedAt == null && p.NormalizedName == key, ct);
        if (clash is not null)
            return OpResult<PlaceDto>.Conflict($"\"{clash.CanonicalName}\" ({clash.Id}) already matches that name; update or alias it, or give this one a distinguishing name.");

        var place = new Place
        {
            Id = Guid.NewGuid(),
            CanonicalName = name,
            NormalizedName = key,
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
            name = PlaceTextNormalizer.Canonical(name);
            if (!string.Equals(place.CanonicalName, name, StringComparison.Ordinal))
            {
                place.CanonicalName = name;
                place.NormalizedName = PlaceTextNormalizer.Key(name);
                db.Record(place.Id, CurationAction.Renamed, actorId, detail: name);
            }
        }

        if (r.Category is { } cat && cat != place.Category)
        {
            place.Category = cat;
            db.Record(place.Id, CurationAction.Recategorized, actorId, detail: cat.ToString());
        }

        if (r.Kind is { } kind && kind != place.Kind)
        {
            place.Kind = kind;
            db.Record(place.Id, CurationAction.Reclassified, actorId, detail: kind.ToString());
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
    /// unchanged on a no-hit or a transient geocoder outage. <paramref name="force"/> bypasses and overwrites the
    /// frozen geocode cache — the only way to heal a stub whose empty answer got frozen.</summary>
    public async Task<OpResult<PlaceDto>> RegeocodeAsync(Guid id, Guid actorId, bool force = false, CancellationToken ct = default)
    {
        var (status, place, error) = await RegeocodeCoreAsync(id, actorId, force, ct);
        switch (status)
        {
            case RegeocodeStatus.NotFound: return OpResult<PlaceDto>.NotFound();
            case RegeocodeStatus.Conflict: return OpResult<PlaceDto>.Conflict(error!);
            case RegeocodeStatus.Healed: break;
            default: return OpResult<PlaceDto>.Invalid(error!);
        }

        var dto = place!.ToDto();
        dto.Containment = await ContainmentAsync(place.WithinAreaId, ct);
        return OpResult<PlaceDto>.Ok(dto);
    }

    /// <summary>Regeocode a worklist (max <see cref="MaxBatchRegeocode"/>) — the bulk healing path for
    /// <c>hasCoordinates=false</c> stubs. Per-item outcomes; a no-hit or outage never aborts the rest.</summary>
    public async Task<OpResult<List<RegeocodePlaceResultDto>>> RegeocodeBatchAsync(
        List<Guid> placeIds, Guid actorId, bool force = false, CancellationToken ct = default)
    {
        if (placeIds is not { Count: > 0 }) return OpResult<List<RegeocodePlaceResultDto>>.Invalid("PlaceIds is required.");
        if (placeIds.Count > MaxBatchRegeocode)
            return OpResult<List<RegeocodePlaceResultDto>>.Invalid($"At most {MaxBatchRegeocode} places per batch.");

        var results = new List<RegeocodePlaceResultDto>(placeIds.Count);
        foreach (var id in placeIds.Distinct())
        {
            var (status, place, error) = await RegeocodeCoreAsync(id, actorId, force, ct);
            results.Add(new RegeocodePlaceResultDto
            {
                PlaceId = id,
                Status = status,
                Name = place?.CanonicalName,
                Latitude = place?.Location?.Y,
                Longitude = place?.Location?.X,
                Error = error,
            });
        }

        return OpResult<List<RegeocodePlaceResultDto>>.Ok(results);
    }

    private async Task<(RegeocodeStatus Status, Place? Place, string? Error)> RegeocodeCoreAsync(
        Guid id, Guid actorId, bool force, CancellationToken ct)
    {
        var place = await db.Places.Include(p => p.ExternalIds)
            .FirstOrDefaultAsync(p => p.Id == id && p.MergedIntoId == null && p.DeletedAt == null, ct);
        if (place is null) return (RegeocodeStatus.NotFound, null, "Not found.");

        var query = string.IsNullOrWhiteSpace(place.FormattedAddress) ? place.CanonicalName : place.FormattedAddress!;
        var result = await geocoder.ForwardAsync(query, limit: 1, bypassCache: force, ct: ct);
        if (result.Status == GeocodeStatus.Unavailable)
            return (RegeocodeStatus.Unavailable, null, "Geocoder unavailable; retry.");
        if (result.Hits.FirstOrDefault() is not { } hit)
            return (RegeocodeStatus.NoHit, null, $"No geocode result for \"{query}\".");

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
            // The failed writes stay pending otherwise, and a batch would re-submit them on the next item's save.
            db.ChangeTracker.Clear();
            return (RegeocodeStatus.Conflict, null, "Could not persist the regeocode; its OSM id may already belong to another place.");
        }

        return (RegeocodeStatus.Healed, place, null);
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
        var name = PlaceTextNormalizer.Canonical(r.Name);
        var key = PlaceTextNormalizer.Key(name);

        var place = await db.Places.Include(p => p.Aliases).Include(p => p.ExternalIds)
            .FirstOrDefaultAsync(p => p.Id == placeId, ct);
        if (place is null) return OpResult<PlaceDto>.NotFound();
        if (place.NormalizedName == key)
            return OpResult<PlaceDto>.Conflict("Alias equals the canonical name.");
        if (place.Aliases.Any(a => a.NormalizedName == key))
            return OpResult<PlaceDto>.Conflict("Alias already exists.");

        // Add via the set — a nav-discovered entity with a pre-set Guid key would be treated as an update.
        // Change-tracker fixup puts it into place.Aliases for the DTO below.
        db.PlaceAliases.Add(new PlaceAlias
        {
            Id = Guid.NewGuid(),
            PlaceId = place.Id,
            Name = name,
            NormalizedName = key,
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

    /// <summary>Attach an external gazetteer id (scheme+value) to a place — correcting the reconciliation keys imports
    /// and dedup match against. Excludes tombstones/merged places (an id on a redirect would never surface).</summary>
    public async Task<OpResult<PlaceDto>> AddExternalIdAsync(Guid placeId, AddExternalIdRequest r, Guid actorId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(r.Value)) return OpResult<PlaceDto>.Invalid("Value is required.");
        var value = r.Value.Trim();

        var place = await db.Places.Include(p => p.Aliases).Include(p => p.ExternalIds)
            .FirstOrDefaultAsync(p => p.Id == placeId && p.MergedIntoId == null && p.DeletedAt == null, ct);
        if (place is null) return OpResult<PlaceDto>.NotFound();
        if (place.ExternalIds.Any(x => x.Scheme == r.Scheme && x.Value == value))
            return OpResult<PlaceDto>.Conflict("External id already exists on this place.");

        var ext = new PlaceExternalId { Id = Guid.NewGuid(), PlaceId = place.Id, Scheme = r.Scheme, Value = value };
        place.ExternalIds.Add(ext);
        db.PlaceExternalIds.Add(ext);
        db.Record(place.Id, CurationAction.ExternalIdAdded, actorId, detail: $"{r.Scheme}:{value}");
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // (Scheme, Value) is globally unique; a cross-place collision surfaces only here. Log it (the MCP layer
            // would otherwise hide it) and fail cleanly.
            logger.LogError(ex, "Adding external id {Scheme}:{Value} to place {PlaceId} failed; it may already belong to another place.", r.Scheme, value, placeId);
            return OpResult<PlaceDto>.Conflict("That external id already belongs to another place.");
        }

        var dto = place.ToDto();
        dto.Containment = await ContainmentAsync(place.WithinAreaId, ct);
        return OpResult<PlaceDto>.Ok(dto);
    }

    public async Task<OpResult> RemoveExternalIdAsync(Guid placeId, ExternalScheme scheme, string value, Guid actorId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(value)) return OpResult.Invalid("Value is required.");
        var v = value.Trim();
        var ext = await db.PlaceExternalIds.FirstOrDefaultAsync(x => x.PlaceId == placeId && x.Scheme == scheme && x.Value == v, ct);
        if (ext is null) return OpResult.NotFound();
        db.PlaceExternalIds.Remove(ext);
        db.Record(placeId, CurationAction.ExternalIdRemoved, actorId, detail: $"{scheme}:{v}");
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

    /// <summary>The append-only curation log for a place, oldest first. 404 only when no Places row exists at all —
    /// tombstoned/merged places keep readable history (that is the audit's point).</summary>
    public async Task<OpResult<List<CurationEventDto>>> HistoryAsync(Guid id, CancellationToken ct = default)
    {
        if (!await db.Places.AnyAsync(p => p.Id == id, ct)) return OpResult<List<CurationEventDto>>.NotFound();
        var events = await db.CurationLog.AsNoTracking()
            .Where(x => x.PlaceId == id)
            .OrderBy(x => x.Seq)
            .Select(x => new CurationEventDto
            {
                Seq = x.Seq,
                Action = x.Action,
                ActorPrincipalId = x.ActorPrincipalId,
                At = x.At,
                RelatedPlaceId = x.RelatedPlaceId,
                Detail = x.Detail,
            })
            .ToListAsync(ct);
        return OpResult<List<CurationEventDto>>.Ok(events);
    }

    /// <summary>Create/dedupe a place from one specific geocode hit picked by OSM identity. Serves the SPA's
    /// commit path after a forward-geocode preview: the hits are already frozen in the cache, so this re-reads
    /// them and funnels into the resolver's dedupe — zero extra geocoder calls.</summary>
    public async Task<OpResult<ResolvePlaceResponse>> CreateFromGeocodeAsync(
        CreatePlaceFromGeocodeRequest r, Guid createdBy, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(r.Query)) return OpResult<ResolvePlaceResponse>.Invalid("Query is required.");
        if (string.IsNullOrWhiteSpace(r.OsmType)) return OpResult<ResolvePlaceResponse>.Invalid("OsmType is required.");

        var result = await geocoder.ForwardAsync(r.Query, limit: 5, ct: ct);
        if (result.Status == GeocodeStatus.Unavailable)
            return OpResult<ResolvePlaceResponse>.Invalid("Geocoder unavailable; retry.");
        var hit = result.Hits.FirstOrDefault(h =>
            h.OsmId == r.OsmId && string.Equals(h.OsmType, r.OsmType, StringComparison.OrdinalIgnoreCase));
        if (hit is null)
            return OpResult<ResolvePlaceResponse>.Invalid("That result is not among the geocode hits for this query.");

        var name = PlaceTextNormalizer.Canonical(r.Name ?? r.Query);
        ResolveOutcome outcome;
        try
        {
            outcome = await resolver.ResolveFromHitAsync(name, hit, createdBy, ct);
        }
        catch (DbUpdateException ex)
        {
            logger.LogError(ex, "Creating a place from geocode hit {OsmType}/{OsmId} failed to persist.", r.OsmType, r.OsmId);
            return OpResult<ResolvePlaceResponse>.Conflict("Could not persist the place; a conflicting gazetteer entry may already exist.");
        }

        return OpResult<ResolvePlaceResponse>.Ok(new ResolvePlaceResponse
        {
            Resolution = outcome.Resolution,
            PlaceId = outcome.Place?.Id,
            Name = outcome.Place?.CanonicalName ?? name,
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

    /// <summary>Bulk get-by-ids (max <see cref="MaxResults"/>): one query per merge-chain depth rather than per id.
    /// Same per-id semantics as <see cref="GetAsync"/> — merge redirects followed to the survivor, unknown and
    /// soft-deleted ids yield a null place — but containment is omitted. Responses align index-for-index.</summary>
    public async Task<OpResult<List<PlaceLookupItemDto>>> LookupAsync(List<Guid> ids, CancellationToken ct = default)
    {
        if (ids is not { Count: > 0 }) return OpResult<List<PlaceLookupItemDto>>.Invalid("Ids is required.");
        if (ids.Count > MaxResults)
            return OpResult<List<PlaceLookupItemDto>>.Invalid($"At most {MaxResults} ids per lookup.");

        var loaded = new Dictionary<Guid, Place>();
        var seen = new HashSet<Guid>(ids);
        var frontier = seen.ToList();
        while (frontier.Count > 0)
        {
            var batch = await db.Places.AsNoTracking()
                .Include(p => p.Aliases).Include(p => p.ExternalIds)
                .Where(p => frontier.Contains(p.Id))
                .ToListAsync(ct);
            foreach (var place in batch) loaded[place.Id] = place;
            frontier = batch.Select(p => p.MergedIntoId).OfType<Guid>().Where(seen.Add).ToList();
        }

        var items = new List<PlaceLookupItemDto>(ids.Count);
        foreach (var requested in ids)
        {
            var walked = new HashSet<Guid>();
            Place? survivor = null;
            Guid? cursor = requested;
            while (cursor is { } cid && walked.Add(cid) && loaded.TryGetValue(cid, out var place))
            {
                if (place.DeletedAt is not null) break;
                if (place.MergedIntoId is null)
                {
                    survivor = place;
                    break;
                }

                cursor = place.MergedIntoId;
            }

            items.Add(new PlaceLookupItemDto { RequestedId = requested, Place = survivor?.ToDto() });
        }

        return OpResult<List<PlaceLookupItemDto>>.Ok(items);
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
