using LupiraGeoApi.Data;
using Marten;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace LupiraGeoApi.IntegrationTests;

/// <summary>
/// Hosts the real app against an ephemeral <b>PostGIS</b> Postgres (Testcontainers — the geo schema needs the postgis +
/// pg_trgm extensions). Runs in <c>Development</c> so the dev auth handler (<c>X-Dev-User</c>) is wired. Both the Marten
/// <c>geo_user</c> schema and the EF <c>geo</c> schema are applied once; data is reset per test. Nominatim is left unset,
/// so geocoding is disabled and the resolver provisions user places (no network in tests).
/// </summary>
public sealed class GeoApiTestFactory : WebApplicationFactory<Program>
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgis/postgis:17-3.5").Build();
    private bool _schemaApplied;

    public GeoApiTestFactory() => _postgres.StartAsync().GetAwaiter().GetResult();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration(cfg =>
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = _postgres.GetConnectionString(),
            }));
    }

    public IDocumentStore Store => Services.GetRequiredService<IDocumentStore>();

    /// <summary>Ensure both schemas exist (once), then wipe all Marten documents and all EF gazetteer rows.</summary>
    public async Task ResetAsync()
    {
        if (!_schemaApplied)
        {
            await Store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
            using var initScope = Services.CreateScope();
            await initScope.ServiceProvider.GetRequiredService<GeoDbContext>().Database.MigrateAsync();
            _schemaApplied = true;
        }
        await Store.Advanced.ResetAllData();
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GeoDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE geo.\"Places\", geo.\"PlaceAliases\", geo.\"PlaceExternalIds\", geo.\"AdminAreas\" CASCADE");
    }

    public HttpClient ApiClient(string email)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", email);
        return client;
    }

    /// <summary>A client with no auth header — for asserting unauthenticated requests are rejected.</summary>
    public HttpClient AnonymousClient() => CreateClient();

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _postgres.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
