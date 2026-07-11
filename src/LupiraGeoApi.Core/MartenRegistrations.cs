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

        opts.Schema.For<Principal>().Index(x => x.AuthentikSub).Index(x => x.Email);
        // Optimistic concurrency: a concurrent edit (e.g. two devices) between load and save throws ConcurrencyException.
        opts.Schema.For<SavedPlace>().Index(x => x.PrincipalId).Index(x => x.PlaceId).UseOptimisticConcurrency(true);
        opts.Schema.For<GeocodeCache>();

        return opts;
    }
}
