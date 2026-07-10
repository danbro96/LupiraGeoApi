using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace LupiraGeoApi.IntegrationTests;

/// <summary>In-process Nominatim double on a random loopback port. In hit mode it answers like the public endpoint;
/// in miss mode like a regional instance asked about something outside its extract (empty search array,
/// "Unable to geocode" reverse). Counts requests so tests can assert cache/fallback behaviour.</summary>
public sealed class NominatimStub : IAsyncDisposable
{
    private readonly WebApplication _app;
    private int _searchCalls, _reverseCalls;

    public string BaseUrl { get; private set; } = "";
    public int SearchCalls => Volatile.Read(ref _searchCalls);
    public int ReverseCalls => Volatile.Read(ref _reverseCalls);

    private NominatimStub(WebApplication app) => _app = app;

    public static async Task<NominatimStub> StartAsync(bool returnResults)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var app = builder.Build();
        var stub = new NominatimStub(app);

        app.MapGet("/search", () =>
        {
            Interlocked.Increment(ref stub._searchCalls);
            return Results.Content(returnResults ? SearchHit : "[]", "application/json");
        });
        app.MapGet("/reverse", () =>
        {
            Interlocked.Increment(ref stub._reverseCalls);
            return Results.Content(returnResults ? ReverseHit : """{"error":"Unable to geocode"}""", "application/json");
        });

        await app.StartAsync();
        stub.BaseUrl = app.Urls.First();
        return stub;
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    private const string SearchHit = $"[{ReverseHit}]";

    private const string ReverseHit = """
        {"lat":"35.6595","lon":"139.7005","display_name":"Shibuya Crossing, Shibuya, Tokyo, Japan",
         "name":"Shibuya Crossing","type":"attraction","category":"tourism","osm_type":"node","osm_id":123456,
         "address":{"country_code":"jp","country":"Japan","state":"Tokyo","city":"Shibuya"}}
        """;
}
