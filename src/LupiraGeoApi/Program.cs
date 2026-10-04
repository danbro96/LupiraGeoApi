using Lupira.Auth.Jwt;
using Lupira.Clients.ServiceTokens;
using Lupira.Depz;
using Lupira.Hosting.Defaults;
using Lupira.Hosting.Health;
using Lupira.Hosting.LanEdge;
using Lupira.Hosting.Observability;
using Lupira.Hosting.OpenApi;
using Lupira.Hosting.Problems;
using Lupira.Identity.Marten.AspNetCore;
using Lupira.Mcp;
using Lupira.Postgres.Health;
using LupiraGeoApi.Basemap;
using LupiraGeoApi.Clients;
using LupiraGeoApi.Core.Abstractions;
using LupiraGeoApi.Core.Application.Gazetteer;
using LupiraGeoApi.Core.Application.Geocoding;
using LupiraGeoApi.Core.Application.Places;
using LupiraGeoApi.Core.Data;
using LupiraGeoApi.Dependencies;
using LupiraGeoApi.Endpoints;
using LupiraGeoApi.Handlers;
using LupiraGeoApi.Mcp;
using Marten;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// --- Bounded context: Marten documents (`geo_user` schema) + EF Core gazetteer (`geo` schema, PostGIS) + the
// transport-neutral services. Connection string is read lazily from ConnectionStrings:Postgres inside AddGeoCore. ---
builder.Services.AddGeoCore();

// --- Host-only services: identity (claims -> Core PrincipalDirectory) + the thin REST handlers. ---
builder.Services.AddLupiraCurrentUser();
builder.Services.AddScoped<MeHandler>();
builder.Services.AddScoped<PlacesHandler>();
builder.Services.AddScoped<GeocodeHandler>();
builder.Services.AddScoped<AdminAreasHandler>();
builder.Services.AddScoped<SavedPlacesHandler>();
builder.Services.AddScoped<CurationHandler>();

// --- Place-reference sources for the orphan sweep: sibling APIs' /internal seams (service-authed). Options bind
// lazily (the client reads BaseUrl per call), so unset config ⇒ IsConfigured=false ⇒ the sweep fails closed. ---
builder.Services.Configure<ContactApiOptions>(builder.Configuration.GetSection(ContactApiOptions.SectionName));
builder.Services.AddHttpClient<IContactPlaceReferences, ContactApiClient>().AddLupiraServiceToken<ContactApiOptions>();
builder.Services.Configure<CalendarApiOptions>(builder.Configuration.GetSection(CalendarApiOptions.SectionName));
builder.Services.AddHttpClient<ICalendarPlaceReferences, CalendarApiClient>().AddLupiraServiceToken<CalendarApiOptions>();

// Self-hosted MapLibre basemap (style + pmtiles + glyphs/sprites); serves nothing until Basemap:AssetsPath is set.
builder.Services.Configure<BasemapOptions>(builder.Configuration.GetSection(BasemapOptions.SectionName));

// MCP server for the agent (read-only find/get/reverse-geocode tools), mounted at /mcp over Streamable HTTP.
// LAN/WireGuard-only — not published through the tunnel (see UseLanOnlySurfaces + the MapLupiraMcp call below).
builder.Services.AddLupiraMcp().WithTools<GeoTools>();

builder.AddLupiraDefaults(o =>
{
    o.CaseInsensitiveProperties = true;
    o.ForwardedHeaders = ForwardedHeaders.None;
});

// --- Auth: OIDC JWT for the REST surface. One identity authority (Authentik); the OIDC `sub` is the only
//           cross-service join key. ---
builder.AddLupiraJwt();
var apiSchemes = LupiraJwtSchemes.Api(builder.Environment);
builder.Services.AddAuthorizationBuilder().AddLupiraApiPolicy(apiSchemes);

builder.AddLupiraTelemetry("lupira-geo-api");

builder.Services.AddLupiraHealth().AddReadyCheck<DatabaseReadyCheck>("postgres");

// Non-gating dependency probe (/depz): the geocoder edges, on a dedicated client so probe traffic
// never rides the throttled fallback geocoder.
builder.Services.AddLupiraDepz(o =>
{
    builder.Configuration.GetSection(DepzOptions.SectionName).Bind(o);
    o.ServiceName = "lupira-geo-api";
    o.MeterName = "LupiraGeoApi.Depz";
    o.MetricPrefix = "geo";
});
builder.Services.AddSingleton<IDependencyTargetSource>(sp => new StaticDependencyTargetSource(DependencyTargets.From(
    sp.GetRequiredService<IOptions<NominatimOptions>>(), builder.Configuration)));

builder.Services.AddLupiraProblems();

builder.Services.AddOpenApi("v1", options => options.AddLupiraConventions(o =>
{
    o.Title = "Lupira Geo API";
    o.Description =
        "Gazetteer, geocoding, and saved-places backend for Lupira. " +
        "Authenticate with a Bearer token issued by the OIDC provider (Authentik).";
}));

var app = builder.Build();

// One-shot schema apply (deploy step: `dotnet LupiraGeoApi.dll --apply-schema`). Marten self-applies its `geo_user`
// schema; EF migrations bring up the `geo` gazetteer schema. Prod never auto-migrates on boot — this is deliberate.
if (args.Contains("--apply-schema"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var store = scope.ServiceProvider.GetRequiredService<IDocumentStore>();
    await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
    var db = scope.ServiceProvider.GetRequiredService<GeoDbContext>();
    await db.Database.MigrateAsync();
    var (places, aliases) = await scope.ServiceProvider.GetRequiredService<PlaceNameBackfill>().RunAsync();
    Console.WriteLine($"Schema applied (Marten + EF). Match keys rebuilt: {places} places, {aliases} aliases.");
    return;
}

// One-shot GeoNames seed (deploy step: `dotnet LupiraGeoApi.dll --seed-gazetteer`). Idempotent; downloads from
// Geonames:BaseUrl and tops up the AdminArea reference tree.
if (args.Contains("--seed-gazetteer"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var importer = scope.ServiceProvider.GetRequiredService<GazetteerImporter>();
    var r = await importer.ImportAsync();
    Console.WriteLine($"Gazetteer seeded: {r.Countries} countries, {r.Regions} regions, {r.Localities} localities.");
    return;
}

// In Development, bring the EF gazetteer schema up on boot (Marten self-applies via CreateOrUpdate).
if (app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<GeoDbContext>();
    await db.Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<PlaceNameBackfill>().RunAsync();
}

// LAN-only surfaces (/mcp + its discovery metadata): 404 anything arriving through the tunnel,
// before auth so a tunnelled probe never even receives a challenge.
app.UseLanOnlySurfaces("/mcp", "/curation", "/.well-known/oauth-protected-resource");

app.UseLupiraDefaults();
app.UseExceptionHandler();

app.UseAuthentication();
app.UseAuthorization();

app.MapLupiraOpenApi(o => o.Title = "Lupira Geo API");

app.MapLupiraHealth();

// REST surface.
app.MapDepz();
app.MapLupiraPing("ApiPolicy");
app.MapMe();
app.MapPlaces();
app.MapCuration();
app.MapGeocode();
app.MapAdminAreas();
app.MapSavedPlaces();
app.MapBasemap();

// Agent MCP transport (LAN/WireGuard-only; excluded from the Cloudflare Tunnel at the edge).
// RFC 9728 metadata lets MCP clients discover the Authentik issuer from the 401 challenge.
app.MapMcpResourceMetadata(app.Configuration["Auth:Oidc:Authority"]);
app.MapLupiraMcp();

app.Run();

// Exposes the implicit Program entry point to the integration test assembly (WebApplicationFactory<Program>).
public partial class Program;
