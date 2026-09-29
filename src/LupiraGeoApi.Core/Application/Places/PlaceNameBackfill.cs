using LupiraGeoApi.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace LupiraGeoApi.Core.Application.Places;

/// <summary>Recomputes every stored match key from its label. Run on schema apply: rows written before the column
/// existed have none, and a normalizer change silently strands the rest. Keys must be computed in C#, not SQL, or
/// the stored key drifts from what a resolve computes.</summary>
public sealed class PlaceNameBackfill(GeoDbContext db)
{
    public async Task<(int Places, int Aliases)> RunAsync(CancellationToken ct = default)
    {
        var places = 0;
        foreach (var place in await db.Places.ToListAsync(ct))
        {
            var key = PlaceTextNormalizer.Key(place.CanonicalName);
            if (place.NormalizedName == key) continue;
            place.NormalizedName = key;
            places++;
        }

        var aliases = 0;
        foreach (var alias in await db.PlaceAliases.ToListAsync(ct))
        {
            var key = PlaceTextNormalizer.Key(alias.Name);
            if (alias.NormalizedName == key) continue;
            alias.NormalizedName = key;
            aliases++;
        }

        if (places + aliases > 0) await db.SaveChangesAsync(ct);
        return (places, aliases);
    }
}
