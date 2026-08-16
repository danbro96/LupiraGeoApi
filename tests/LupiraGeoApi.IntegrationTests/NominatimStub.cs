using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace LupiraGeoApi.IntegrationTests;

/// <summary>In-process Nominatim double on a random loopback port. In hit mode it answers like the public endpoint;
/// in miss mode like a regional instance asked about something outside its extract: an empty search array, and a
/// reverse that country-matches via the worldwide country_osm_grid (a country centroid <em>with</em> a lat — not
/// "Unable to geocode"), which must not suppress the fallback. Counts requests so tests can assert cache/fallback
/// behaviour.</summary>
public sealed class NominatimStub : IAsyncDisposable
{
    private readonly WebApplication _app;
    private int _searchCalls, _reverseCalls;

    public string BaseUrl { get; private set; } = "";
    public int SearchCalls => Volatile.Read(ref _searchCalls);
    public int ReverseCalls => Volatile.Read(ref _reverseCalls);

    /// <summary>Hit/miss mode — mutable so a test can flip an endpoint from empty to answering (heal scenarios).</summary>
    public bool ReturnResults { get; set; }

    /// <summary>When true, /search answers with two hits instead of one (pick-a-hit scenarios).</summary>
    public bool MultiHit { get; set; }

    private NominatimStub(WebApplication app) => _app = app;

    /// <summary>Start a stub. In <paramref name="failStatus"/> mode every request answers with that HTTP status (a
    /// transient outage, e.g. 503) — for exercising retry + the "geocoder unavailable, create nothing" path.</summary>
    public static async Task<NominatimStub> StartAsync(bool returnResults, int? failStatus = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var app = builder.Build();
        var stub = new NominatimStub(app) { ReturnResults = returnResults };

        app.MapGet("/search", () =>
        {
            Interlocked.Increment(ref stub._searchCalls);
            return failStatus is { } s ? Results.StatusCode(s)
                : Results.Content(stub.ReturnResults ? (stub.MultiHit ? MultiSearchHit : SearchHit) : "[]", "application/json");
        });
        app.MapGet("/reverse", () =>
        {
            Interlocked.Increment(ref stub._reverseCalls);
            return failStatus is { } s ? Results.StatusCode(s) : Results.Content(stub.ReturnResults ? ReverseHit : CountryReverse, "application/json");
        });

        await app.StartAsync();
        stub.BaseUrl = app.Urls.First();
        return stub;
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    private const string SearchHit = $"[{ReverseHit}]";

    private const string MultiSearchHit = $"[{ReverseHit},{SecondHit}]";

    private const string SecondHit = """
        {"lat":"35.6684","lon":"139.6833","display_name":"Shibuya Station, Shibuya, Tokyo, Japan",
         "name":"Shibuya Station","type":"station","category":"railway","osm_type":"way","osm_id":654321,
         "address":{"country_code":"jp","country":"Japan","state":"Tokyo","city":"Shibuya"}}
        """;

    private const string ReverseHit = """
        {"lat":"35.6595","lon":"139.7005","display_name":"Shibuya Crossing, Shibuya, Tokyo, Japan",
         "name":"Shibuya Crossing","type":"attraction","category":"tourism","osm_type":"node","osm_id":123456,
         "address":{"country_code":"jp","country":"Japan","state":"Tokyo","city":"Shibuya"}}
        """;

    // What a regional instance actually returns for an out-of-coverage point: a country_osm_grid centroid, country-only.
    private const string CountryReverse = """
        {"lat":"36.5748441","lon":"139.2394179","display_name":"Japan","name":"Japan","addresstype":"country",
         "category":"boundary","type":"administrative","place_rank":4,"address":{"country":"Japan","country_code":"jp"}}
        """;
}
