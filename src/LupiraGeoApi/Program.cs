using System.Text.Json.Serialization;
using LupiraGeoApi.Application;
using LupiraGeoApi.Auth;
using LupiraGeoApi.Data;
using LupiraGeoApi.Endpoints;
using LupiraGeoApi.Handlers;
using LupiraGeoApi.Health;
using LupiraGeoApi.Mcp;
using Marten;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// --- Bounded context: Marten documents (`geo_user` schema) + EF Core gazetteer (`geo` schema, PostGIS) + the
// transport-neutral services. Connection string is read lazily from ConnectionStrings:Postgres inside AddGeoCore. ---
builder.Services.AddGeoCore();

// --- Host-only services: identity (claims -> Core PrincipalDirectory) + the thin REST handlers. ---
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddScoped<MeHandler>();
builder.Services.AddScoped<PlacesHandler>();
builder.Services.AddScoped<GeocodeHandler>();
builder.Services.AddScoped<AdminAreasHandler>();
builder.Services.AddScoped<SavedPlacesHandler>();

// MCP server for the agent (read-only find/get/reverse-geocode tools), mounted at /mcp over Streamable HTTP.
// LAN/WireGuard-only — not published through the tunnel (see UseMcpLanOnly + the MapMcp call below).
builder.Services.AddMcpServer().WithHttpTransport().WithTools<GeoTools>();

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// --- Auth: OIDC JWT for the REST surface. One identity authority (Authentik); the OIDC `sub` is the only
//           cross-service join key. ---
var authBuilder = builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Auth:Authority"];
        options.Audience = builder.Configuration["Auth:Audience"];
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        options.Events = new JwtBearerEvents
        {
            // MCP auth spec: a 401 on /mcp advertises the RFC 9728 metadata so clients can discover the issuer.
            OnChallenge = ctx =>
            {
                if (ctx.Request.Path.StartsWithSegments("/mcp"))
                    ctx.Response.Headers.Append("WWW-Authenticate",
                        $"Bearer resource_metadata=\"{McpResourceMetadata.ResourceMetadataUrl(ctx.Request)}\"");
                return Task.CompletedTask;
            },
        };
    });

// Development-only: allow X-Dev-User header auth so the API can be exercised without Authentik.
if (builder.Environment.IsDevelopment())
    authBuilder.AddScheme<AuthenticationSchemeOptions, DevAuthHandler>(DevAuthHandler.SchemeName, _ => { });

string[] apiSchemes = builder.Environment.IsDevelopment()
    ? [JwtBearerDefaults.AuthenticationScheme, DevAuthHandler.SchemeName]
    : [JwtBearerDefaults.AuthenticationScheme];

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("ApiPolicy", p => p.AddAuthenticationSchemes(apiSchemes).RequireAuthenticatedUser());

// --- Observability: OpenTelemetry -> OpenObserve. Env-gated; the OTLP exporter reads OTEL_EXPORTER_OTLP_* itself. ---
var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("lupira-geo-api"))
    .WithTracing(t =>
    {
        // Health probes are polled constantly by docker + devops-monitor; their spans add nothing.
        t.AddAspNetCoreInstrumentation(o => o.Filter = ctx =>
            ctx.Request.Path != "/livez" && ctx.Request.Path != "/readyz" && ctx.Request.Path != "/pingz");
        t.AddHttpClientInstrumentation();
        if (!string.IsNullOrWhiteSpace(otlpEndpoint)) t.AddOtlpExporter();
    })
    .WithMetrics(m =>
    {
        m.AddAspNetCoreInstrumentation();
        m.AddHttpClientInstrumentation();
        m.AddRuntimeInstrumentation();
        if (!string.IsNullOrWhiteSpace(otlpEndpoint)) m.AddOtlpExporter();
    });

builder.Logging.AddOpenTelemetry(o =>
{
    o.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("lupira-geo-api"));
    o.IncludeScopes = true;
    o.IncludeFormattedMessage = true;
    if (!string.IsNullOrWhiteSpace(otlpEndpoint)) o.AddOtlpExporter();
});

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseReadyCheck>("postgres", tags: ["ready"]);

builder.Services.AddOpenApi("v1", options =>
{
    options.AddDocumentTransformer((document, context, _) =>
    {
        document.Info = new()
        {
            Title = "Lupira Geo API",
            Version = "v1",
            Description =
                "Gazetteer, geocoding, and saved-places backend for Lupira. " +
                "Authenticate with a Bearer token issued by the OIDC provider (Authentik).",
        };
        document.Components ??= new();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "OIDC bearer token. Send as `Authorization: Bearer <token>`.",
        };
        return Task.CompletedTask;
    });
    options.AddOperationTransformer((operation, context, _) =>
    {
        var endpointMetadata = context.Description.ActionDescriptor.EndpointMetadata;
        var requiresAuth = endpointMetadata.OfType<IAuthorizeData>().Any()
                        && !endpointMetadata.OfType<IAllowAnonymous>().Any();
        if (requiresAuth)
        {
            operation.Security ??= new List<OpenApiSecurityRequirement>();
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", context.Document)] = new List<string>(),
            });
        }

        return Task.CompletedTask;
    });
});

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
    Console.WriteLine("Schema applied (Marten + EF).");
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
}

app.UseAuthentication();
app.UseAuthorization();

// Defence-in-depth: 404 any /mcp request that arrives bearing Cloudflare edge headers.
app.UseMcpLanOnly();

app.MapOpenApi("/openapi/{documentName}.json").AllowAnonymous();
app.MapScalarApiReference("/scalar", o => o
        .WithTitle("Lupira Geo API")
        .WithTheme(ScalarTheme.BluePlanet))
    .AllowAnonymous();

app.MapGet("/", () => TypedResults.Redirect("/scalar"))
   .ExcludeFromDescription()
   .AllowAnonymous();

// Health probes: /livez = liveness (no dependency checks); /readyz = readiness (Postgres reachable).
app.MapHealthChecks("/livez", new HealthCheckOptions { Predicate = _ => false })
    .DisableHttpMetrics();
app.MapHealthChecks("/readyz", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") })
    .DisableHttpMetrics();

// REST surface.
app.MapPing();
app.MapMe();
app.MapPlaces();
app.MapGeocode();
app.MapAdminAreas();
app.MapSavedPlaces();

// Agent MCP transport (LAN/WireGuard-only; excluded from the Cloudflare Tunnel at the edge).
// RFC 9728 metadata lets MCP clients discover the Authentik issuer from the 401 challenge.
app.MapMcpResourceMetadata(app.Configuration["Auth:Authority"]);
app.MapMcp("/mcp").RequireAuthorization("ApiPolicy");

app.Run();

// Exposes the implicit Program entry point to the integration test assembly (WebApplicationFactory<Program>).
public partial class Program;
