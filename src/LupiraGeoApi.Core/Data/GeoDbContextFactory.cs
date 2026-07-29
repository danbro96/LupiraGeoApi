using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore;

namespace LupiraGeoApi.Data;

/// <summary>
/// Design-time factory so <c>dotnet ef migrations</c> can build the context without booting the host. Uses a local dev
/// connection (overridable via <c>GEO_DESIGN_CONNECTION</c>) — migrations are authored offline and applied only to
/// Testcontainers / a local PG, never the live DB.
/// </summary>
public sealed class GeoDbContextFactory : IDesignTimeDbContextFactory<GeoDbContext>
{
    public GeoDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("GEO_DESIGN_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=lupira_geo;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<GeoDbContext>()
            .UseNpgsql(connection, o =>
            {
                o.UseNetTopologySuite();
                o.MigrationsHistoryTable("__ef_migrations_history", GeoDbContext.Schema);
            })
            .Options;

        return new GeoDbContext(options);
    }
}
