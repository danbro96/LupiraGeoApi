using JasperFx;
using LupiraGeoApi.Application;
using LupiraGeoApi.Data;
using LupiraGeoApi.Domain;
using Marten;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers the LupiraGeoApi bounded context into the host's DI container: the Marten document store
/// (<c>geo_user</c> schema — identity, saved places, geocode cache), the EF Core <c>GeoDbContext</c> (<c>geo</c> schema —
/// gazetteer + admin reference data on PostGIS), and the transport-neutral services. Both engines share one connection
/// string (<c>ConnectionStrings:Postgres</c>) against the same database but never overlap (disjoint schemas).</summary>
public static class CoreServiceCollectionExtensions
{
    public const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=lupira_geo;Username=lupira_geo_user;Password=devpassword";

    public static IServiceCollection AddGeoCore(this IServiceCollection services)
    {
        // Resolve the connection string lazily from IConfiguration so test hosts (WebApplicationFactory) can override
        // ConnectionStrings:Postgres before the stores are built.
        services.AddMarten(sp =>
        {
            var cs = sp.GetRequiredService<IConfiguration>().GetConnectionString("Postgres") ?? DefaultConnectionString;
            var opts = new StoreOptions();
            opts.Connection(cs);
            opts.UseLupiraGeo();
            // Schema is a deliberate --apply-schema step in prod (postgres-marten-prod.md); dev self-applies on boot.
            opts.AutoCreateSchemaObjects = sp.GetRequiredService<IHostEnvironment>().IsDevelopment()
                ? AutoCreate.CreateOrUpdate
                : AutoCreate.None;
            return opts;
        }).UseLightweightSessions();

        services.AddDbContext<GeoDbContext>((sp, opts) =>
        {
            var cs = sp.GetRequiredService<IConfiguration>().GetConnectionString("Postgres") ?? DefaultConnectionString;
            opts.UseNpgsql(cs, o =>
            {
                o.UseNetTopologySuite();
                o.MigrationsHistoryTable("__ef_migrations_history", GeoDbContext.Schema);
            });
        });

        services.AddScoped<PrincipalDirectory>();
        services.AddGeoServices();
        return services;
    }
}
