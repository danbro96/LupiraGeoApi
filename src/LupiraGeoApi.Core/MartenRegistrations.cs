using LupiraGeoApi.Domain;
using Marten;
using Weasel.Core;

namespace LupiraGeoApi.Domain;

/// <summary>Configures the Marten store in the <c>geo_user</c> schema: plain documents for per-principal user state and
/// caches (identity, saved places, the geocode cache). The gazetteer + admin reference data live in a disjoint <c>geo</c>
/// schema owned by EF Core (<see cref="LupiraGeoApi.Data.GeoDbContext"/>), which Marten's schema-diff never touches.
/// Enums serialize as strings.</summary>
public static class MartenRegistrations
{
    public static StoreOptions UseLupiraGeo(this StoreOptions opts)
    {
        opts.DatabaseSchemaName = "geo_user";
        opts.UseSystemTextJsonForSerialization(EnumStorage.AsString);

        // The Authentik sub is the resolution anchor and is unique — without the constraint, concurrent
        // first-sight logins each insert their own row and the caller silently resolves to whichever one
        // Postgres returns first. Email stays non-unique: it is mutable, and an `email|{email}` placeholder
        // row legitimately shares an email with its real-sub counterpart until the upgrade lands.
        opts.Schema.For<Principal>().Index(x => x.AuthentikSub, i => i.IsUnique = true).Index(x => x.Email);
        // Optimistic concurrency: a concurrent edit (e.g. two devices) between load and save throws ConcurrencyException.
        opts.Schema.For<SavedPlace>().Index(x => x.PrincipalId).Index(x => x.PlaceId).UseOptimisticConcurrency(true);
        opts.Schema.For<GeocodeCache>();

        return opts;
    }
}
