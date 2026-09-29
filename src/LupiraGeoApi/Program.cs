using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Serialization;
using LupiraGeoApi.Auth;
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
using LupiraGeoApi.Http;
using LupiraGeoApi.Mcp;
using Marten;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
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
builder.Services.AddScoped<CurationHandler>();

// --- Place-reference sources for the orphan sweep: sibling APIs' /internal seams (service-authed). Options bind
// lazily (the client reads BaseUrl per call), so unset config ⇒ IsConfigured=false ⇒ the sweep fails closed. ---
builder.Services.Configure<ContactApiOptions>(builder.Configuration.GetSection(ContactApiOptions.SectionName));
builder.Services.AddHttpClient<IContactPlaceReferences, ContactApiClient>();
builder.Services.Configure<CalendarApiOptions>(builder.Configuration.GetSection(CalendarApiOptions.SectionName));
builder.Services.AddHttpClient<ICalendarPlaceReferences, CalendarApiClient>();

// Self-hosted MapLibre basemap (style + pmtiles + glyphs/sprites); serves nothing until Basemap:AssetsPath is set.
builder.Services.Configure<BasemapOptions>(builder.Configuration.GetSection(BasemapOptions.SectionName));

// MCP server for the agent (read-only find/get/reverse-geocode tools), mounted at /mcp over Streamable HTTP.
// LAN/WireGuard-only — not published through the tunnel (see UseLanOnlySurfaces + the MapMcp call below).
builder.Services.AddMcpServer().WithHttpTransport().WithTools<GeoTools>();

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// --- Auth: OIDC JWT for the REST surface. One identity authority (Authentik); the OIDC `sub` is the only
//           cross-service join key. ---
// `dotnet build` regenerates openapi/ via getdocument, which boots this Program with no real config —
// skip the guard there (and in Development, where the dev-header scheme needs no authority).
var isOpenApiBuild = Environment.GetCommandLineArgs()
    .Any(a => a.Contains("getdocument", StringComparison.OrdinalIgnoreCase));

var oidc = builder.Configuration.GetSection(OidcAuthOptions.SectionName).Get<OidcAuthOptions>() ?? new OidcAuthOptions();
if (!isOpenApiBuild && !builder.Environment.IsDevelopment()
    && (string.IsNullOrWhiteSpace(oidc.Authority) || string.IsNullOrWhiteSpace(oidc.Audience)))
    throw new InvalidOperationException("Auth:Oidc Authority + Audience are required outside Development.");

var authBuilder = builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = oidc.Authority;
        options.Audience = oidc.Audience;
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        options.Events = new JwtBearerEvents
        {
            // MCP auth spec: a 401 on /mcp advertises the RFC 9728 metadata so clients can discover the
            // issuer. HandleResponse suppresses the default bare "Bearer" header so exactly one goes out.
            OnChallenge = ctx =>
            {
                if (ctx.Request.Path.StartsWithSegments("/mcp"))
                {
                    ctx.HandleResponse();
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    ctx.Response.Headers.WWWAuthenticate =
                        $"Bearer resource_metadata=\"{McpResourceMetadata.ResourceMetadataUrl(ctx.Request)}\"";
                }

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
            ctx.Request.Path != "/livez" && ctx.Request.Path != "/readyz" && ctx.Request.Path != "/pingz"
            && ctx.Request.Path != "/depz");
        t.AddHttpClientInstrumentation();
        if (!string.IsNullOrWhiteSpace(otlpEndpoint)) t.AddOtlpExporter();
    })
    .WithMetrics(m =>
    {
        m.AddMeter("LupiraGeoApi.*");
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

builder.Services.AddAppHealthChecks();

// Non-gating dependency probe (/depz): the geocoder edges, on a dedicated client so probe traffic
// never rides the throttled fallback geocoder.
builder.Services.Configure<DepzOptions>(builder.Configuration.GetSection(DepzOptions.SectionName));
var depzOptions = builder.Configuration.GetSection(DepzOptions.SectionName).Get<DepzOptions>() ?? new DepzOptions();
builder.Services.AddSingleton(sp => DependencyTargets.From(
    sp.GetRequiredService<IOptions<NominatimOptions>>(), builder.Configuration));
builder.Services.AddSingleton<DependencyReportCache>();
builder.Services.AddSingleton<DependencyProbe>();
builder.Services.AddHttpClient(DependencyProbe.ProbeClientName, c => c.Timeout = depzOptions.ProbeTimeout);
if (depzOptions.Enabled)
    builder.Services.AddHostedService<DependencyPollWorker>();

builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
    ctx.ProblemDetails.Extensions["traceId"] = Activity.Current?.Id ?? ctx.HttpContext.TraceIdentifier);
builder.Services.AddExceptionHandler<ProblemExceptionHandler>();

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
        document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>();
        document.Components.Schemas["ProblemDetails"] = ProblemDetailsSchema();
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
            AddProblem(operation, context.Document, StatusCodes.Status401Unauthorized, "Unauthorized");
        }

        // The cross-cutting code no endpoint declares — ProblemExceptionHandler produces it.

        AddProblem(operation, context.Document, StatusCodes.Status500InternalServerError, "Internal server error");

        // Bodyless 4xx/5xx come from the non-generic arms of the typed-result unions (NotFound,

        // UnauthorizedHttpResult). UseStatusCodePages fills them at runtime, so declare the shape.

        foreach (var code in operation.Responses?.Keys.ToList() ?? [])
        {
            if (code.Length != 3 || code[0] is not ('4' or '5')) continue;

            var existing = operation.Responses![code];

            if (existing.Content is { Count: > 0 }) continue;

            operation.Responses[code] = new OpenApiResponse
            { Description = existing.Description, Content = ProblemContent(context.Document) };
        }

        return Task.CompletedTask;
    });
});

// Every error response carries the same shape, so a generated client types its error once instead of
// falling back to `void`.
static Dictionary<string, OpenApiMediaType> ProblemContent(OpenApiDocument document) =>
    new() { ["application/problem+json"] = new() { Schema = new OpenApiSchemaReference("ProblemDetails", document) } };

static void AddProblem(OpenApiOperation operation, OpenApiDocument document, int status, string description)
{
    var code = status.ToString(CultureInfo.InvariantCulture);
    operation.Responses ??= [];
    if (operation.Responses.ContainsKey(code)) return;
    operation.Responses[code] = new OpenApiResponse { Description = description, Content = ProblemContent(document) };
}

// RFC 9457. Declared here because nothing returns the CLR type directly, so the generator never emits it.
static OpenApiSchema ProblemDetailsSchema() => new()
{
    Type = JsonSchemaType.Object,
    Description = "RFC 9457 problem details.",
    Properties = new Dictionary<string, IOpenApiSchema>
    {
        ["type"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null },
        ["title"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null },
        ["status"] = new OpenApiSchema { Type = JsonSchemaType.Integer | JsonSchemaType.Null, Format = "int32" },
        ["detail"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null },
        ["instance"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null },
        ["traceId"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null },
    },
};

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
app.UseLanOnlySurfaces();

app.UseExceptionHandler();
// Fills the empty body of a bare 4xx (auth challenges, TypedResults.NotFound) with
// ProblemDetails, so the spec's promise holds. Scoped away from /mcp — JSON-RPC has its own error shape.
app.UseWhen(c => !c.Request.Path.StartsWithSegments("/mcp"), b => b.UseStatusCodePages());

app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi("/openapi/{documentName}.json").AllowAnonymous();
app.MapScalarApiReference("/scalar", o => o
        .WithTitle("Lupira Geo API")
        .WithTheme(ScalarTheme.BluePlanet))
    .AllowAnonymous();

app.MapGet("/", () => TypedResults.Redirect("/scalar"))
   .ExcludeFromDescription()
   .AllowAnonymous();

app.MapAppHealthChecks();

// REST surface.
app.MapDepz();
app.MapPing();
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
app.MapMcp("/mcp").RequireAuthorization("ApiPolicy");

app.Run();

// Exposes the implicit Program entry point to the integration test assembly (WebApplicationFactory<Program>).
public partial class Program;
