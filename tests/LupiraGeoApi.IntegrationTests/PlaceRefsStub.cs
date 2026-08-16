using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace LupiraGeoApi.IntegrationTests;

/// <summary>In-process double for the contact + cal place-reference check seams. Counts come from mutable maps so a
/// test can add/remove references between find and prune; per-route fail switches exercise the fail-closed path;
/// request bodies are recorded so tests can assert which ids geo actually asked about.</summary>
public sealed class PlaceRefsStub : IAsyncDisposable
{
    private readonly WebApplication _app;

    public string BaseUrl { get; private set; } = "";

    /// <summary>placeId → contact address count.</summary>
    public ConcurrentDictionary<Guid, int> ContactRefs { get; } = new();

    /// <summary>placeId → (live, deleted) calendar item counts.</summary>
    public ConcurrentDictionary<Guid, (int Live, int Deleted)> CalendarRefs { get; } = new();

    public bool FailContacts { get; set; }
    public bool FailCalendar { get; set; }

    /// <summary>Every id batch received, in order, per seam.</summary>
    public List<List<Guid>> ContactRequests { get; } = [];
    public List<List<Guid>> CalendarRequests { get; } = [];

    private PlaceRefsStub(WebApplication app) => _app = app;

    public static async Task<PlaceRefsStub> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var app = builder.Build();
        var stub = new PlaceRefsStub(app);

        app.MapPost("/internal/contacts/place-references:check", async (HttpContext ctx) =>
        {
            if (stub.FailContacts) return Results.StatusCode(500);
            var ids = await ReadIdsAsync(ctx);
            lock (stub.ContactRequests) stub.ContactRequests.Add(ids);
            var places = ids.Where(stub.ContactRefs.ContainsKey)
                .Select(id => new { placeId = id, count = stub.ContactRefs[id] });
            return Results.Json(new { places });
        });

        app.MapPost("/internal/items/place-references:check", async (HttpContext ctx) =>
        {
            if (stub.FailCalendar) return Results.StatusCode(500);
            var ids = await ReadIdsAsync(ctx);
            lock (stub.CalendarRequests) stub.CalendarRequests.Add(ids);
            var places = ids.Where(stub.CalendarRefs.ContainsKey)
                .Select(id => new { placeId = id, liveCount = stub.CalendarRefs[id].Live, deletedCount = stub.CalendarRefs[id].Deleted });
            return Results.Json(new { places });
        });

        await app.StartAsync();
        stub.BaseUrl = app.Urls.First();
        return stub;
    }

    private static async Task<List<Guid>> ReadIdsAsync(HttpContext ctx)
    {
        using var doc = await JsonDocument.ParseAsync(ctx.Request.Body);
        return [.. doc.RootElement.GetProperty("placeIds").EnumerateArray().Select(e => e.GetGuid())];
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();
}
