using Lupira.Testing.Postgres;
using LupiraGeoApi.Core.Data;
using Marten;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LupiraGeoApi.IntegrationTests;

/// <summary>
/// Hosts the real app against an ephemeral <b>PostGIS</b> Postgres (Testcontainers — the geo schema needs the postgis +
/// pg_trgm extensions). Runs in <c>Development</c> so the dev auth handler (<c>X-Dev-User</c>) is wired. Both the Marten
/// <c>geo_user</c> schema and the EF <c>geo</c> schema are applied once; data is reset per test. Nominatim is left unset,
/// so geocoding is disabled and the resolver provisions user places (no network in tests).
/// </summary>
public sealed class GeoApiTestFactory() : LupiraApiFactory<Program>(PostgresImages.PostGis)
{
    /// <summary>Extra config keys (e.g. Nominatim stub URLs) — populate before the first client is created.</summary>
    public Dictionary<string, string?> ExtraConfig { get; } = [];

    protected override string AuthentikSlug => "lupira-geo";

    public IDocumentStore Store => Services.GetRequiredService<IDocumentStore>();

    protected override void AddSettings(IDictionary<string, string?> settings)
    {
        foreach (var (key, value) in ExtraConfig) settings[key] = value;
    }

    protected override async Task ApplySchemaAsync()
    {
        await Store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<GeoDbContext>().Database.MigrateAsync();
    }

    protected override async Task ResetDataAsync()
    {
        await Store.Advanced.ResetAllData();
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GeoDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE geo.\"Places\", geo.\"PlaceAliases\", geo.\"PlaceExternalIds\", geo.\"AdminAreas\", geo.\"CurationLog\" CASCADE");
    }
}
