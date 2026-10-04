using Lupira.Results;
using LupiraGeoApi.Core.Data;
using LupiraGeoApi.Core.Domain;
using LupiraGeoApi.Core.Dtos.Places;
using LupiraGeoApi.Core.Mappers;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace LupiraGeoApi.Core.Application.Places;

/// <summary>Finds duplicate candidates for the curation pass. Read-only on purpose: co-located places with unlike
/// names are as often an apartment or a mall unit as a duplicate, so the call is a human's.</summary>
public sealed class PlaceDuplicateService(GeoDbContext db)
{
    public const int MaxClusters = 200;

    private const double DefaultRadiusMeters = 25;
    private const double DefaultSimilarity = 0.3;

    public async Task<OpResult<List<DuplicateClusterDto>>> FindAsync(
        double? radiusM, double? minSimilarity, int? limit, CancellationToken ct = default)
    {
        var radius = radiusM ?? DefaultRadiusMeters;
        var similarity = minSimilarity ?? DefaultSimilarity;
        if (radius is <= 0 or > 500) return OpResult<List<DuplicateClusterDto>>.Invalid("radiusM must be between 0 and 500.");
        if (similarity is < 0 or > 1) return OpResult<List<DuplicateClusterDto>>.Invalid("minSimilarity must be between 0 and 1.");
        var take = Math.Clamp(limit ?? 50, 1, MaxClusters);

        var live = db.Places.AsNoTracking().Where(p => p.MergedIntoId == null && p.DeletedAt == null);

        var sameNameKeys = await live
            .GroupBy(p => p.NormalizedName)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToListAsync(ct);

        var clusters = new List<DuplicateClusterDto>();
        if (sameNameKeys.Count > 0)
        {
            var sameName = await live.Where(p => sameNameKeys.Contains(p.NormalizedName)).ToListAsync(ct);
            clusters.AddRange(sameName
                .GroupBy(p => p.NormalizedName)
                .Select(g => Cluster(DuplicateReason.SameName, [.. g])));
        }

        // Self-join rather than a window function: EF can express it, and the GiST index means the spatial predicate
        // narrows to a handful of rows before any trigram scoring happens.
        var pairs = await live
            .Where(p => p.Location != null)
            .SelectMany(p => db.Places.AsNoTracking()
                .Where(q => q.MergedIntoId == null && q.DeletedAt == null && q.Location != null
                    && q.Id != p.Id
                    && q.NormalizedName != p.NormalizedName
                    && q.Location!.Distance(p.Location!) <= radius
                    && (EF.Functions.TrigramsWordSimilarity(p.NormalizedName, q.NormalizedName) >= similarity
                        || EF.Functions.TrigramsWordSimilarity(q.NormalizedName, p.NormalizedName) >= similarity))
                .Select(q => new { Left = p, Right = q }))
            .ToListAsync(ct);

        var seen = new HashSet<(Guid, Guid)>();
        foreach (var pair in pairs)
        {
            var key = pair.Left.Id.CompareTo(pair.Right.Id) < 0
                ? (pair.Left.Id, pair.Right.Id)
                : (pair.Right.Id, pair.Left.Id);
            if (!seen.Add(key)) continue;
            clusters.Add(Cluster(DuplicateReason.CoLocated, [pair.Left, pair.Right]));
        }

        var ordered = clusters
            .OrderBy(c => c.Reason)
            .ThenBy(c => c.SpreadM ?? double.MaxValue)
            .Take(take)
            .ToList();
        return OpResult<List<DuplicateClusterDto>>.Ok(ordered);
    }

    private static DuplicateClusterDto Cluster(DuplicateReason reason, List<Place> places)
    {
        var located = places.Where(p => p.Location is not null).ToList();
        double? spread = null;
        for (var i = 0; i < located.Count; i++)
            for (var j = i + 1; j < located.Count; j++)
                spread = Math.Max(spread ?? 0, Haversine(located[i].Location!, located[j].Location!));

        return new DuplicateClusterDto
        {
            Reason = reason,
            SpreadM = spread is { } s ? Math.Round(s, 1, MidpointRounding.AwayFromZero) : null,
            Places = places.OrderByDescending(p => p.Verified).ThenBy(p => p.CreatedAt).Select(p => p.ToDto()).ToList(),
        };
    }

    // NTS Distance over these points is planar degrees — the columns are geography, so metres need the real formula.
    private static double Haversine(Point a, Point b)
    {
        const double earthRadiusM = 6371000;
        var dLat = (b.Y - a.Y) * Math.PI / 180;
        var dLon = (b.X - a.X) * Math.PI / 180;
        var lat1 = a.Y * Math.PI / 180;
        var lat2 = b.Y * Math.PI / 180;
        var h = (Math.Sin(dLat / 2) * Math.Sin(dLat / 2))
            + (Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2));
        return 2 * earthRadiusM * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }
}
