using System.Text.Json;
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
    private int _searchCalls, _reverseCalls, _lookupCalls;

    public string BaseUrl { get; private set; } = "";
    public int SearchCalls => Volatile.Read(ref _searchCalls);
    public int ReverseCalls => Volatile.Read(ref _reverseCalls);
    public int LookupCalls => Volatile.Read(ref _lookupCalls);

    /// <summary>Query string of the latest /search and /reverse requests.</summary>
    public string LastSearchQuery { get; private set; } = "";
    public string LastReverseQuery { get; private set; } = "";

    /// <summary>Hit/miss mode — mutable so a test can flip an endpoint from empty to answering (heal scenarios).</summary>
    public bool ReturnResults { get; set; }

    /// <summary>When true, /search answers with two hits instead of one (pick-a-hit scenarios).</summary>
    public bool MultiHit { get; set; }

    /// <summary>When true, /search answers with a city boundary (what free text like "Riga, Latvia" matches).</summary>
    public bool CityHit { get; set; }

    /// <summary>When true, /search answers with an obscure same-named hamlet (a regional answer to a foreign bare name).</summary>
    public bool WeakHit { get; set; }

    /// <summary>When true, /search answers with a bare street (what a numbered address often matches).</summary>
    public bool StreetHit { get; set; }

    /// <summary>When true (and in hit mode), /reverse answers with a numbered house on a street.</summary>
    public bool HouseReverse { get; set; }

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

        app.MapGet("/search", (HttpRequest req) =>
        {
            Interlocked.Increment(ref stub._searchCalls);
            stub.LastSearchQuery = req.QueryString.Value ?? "";
            return failStatus is { } s ? Results.StatusCode(s)
                : Results.Content(!stub.ReturnResults ? "[]" : stub.WeakHit ? WeakSearchHit : stub.CityHit ? CitySearchHit
                    : stub.StreetHit ? StreetSearchHit : stub.MultiHit ? MultiSearchHit : SearchHit, "application/json");
        });
        app.MapGet("/reverse", (HttpRequest req) =>
        {
            Interlocked.Increment(ref stub._reverseCalls);
            stub.LastReverseQuery = req.QueryString.Value ?? "";
            return failStatus is { } s ? Results.StatusCode(s)
                : Results.Content(!stub.ReturnResults ? CountryReverse : stub.HouseReverse ? HouseReverseHit : ReverseHit, "application/json");
        });
        // Hit mode knows every object this stub can return; miss mode knows none.
        app.MapGet("/lookup", (string osm_ids) =>
        {
            Interlocked.Increment(ref stub._lookupCalls);
            if (failStatus is { } s) return Results.StatusCode(s);
            return Results.Content(
                stub.ReturnResults && Known.Value.TryGetValue(osm_ids, out var obj) ? $"[{obj}]" : "[]", "application/json");
        });

        await app.StartAsync();
        stub.BaseUrl = app.Urls.First();
        return stub;
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    private static readonly Lazy<Dictionary<string, string>> Known = new(() =>
    {
        var known = new Dictionary<string, string>();
        foreach (var json in new[] { ReverseHit, SecondHit, StreetSearchHit, CitySearchHit, WeakSearchHit, HouseReverseHit })
        {
            using var doc = JsonDocument.Parse(json);
            var items = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToList() : [doc.RootElement];
            foreach (var el in items)
                known[$"{char.ToUpperInvariant(el.GetProperty("osm_type").GetString()![0])}{el.GetProperty("osm_id").GetInt64()}"] = el.GetRawText();
        }

        return known;
    });

    private const string SearchHit = $"[{ReverseHit}]";

    private const string MultiSearchHit = $"[{ReverseHit},{SecondHit}]";

    public const long CityOsmId = 13048688;

    private const string CitySearchHit = """
        [{"lat":"56.9493977","lon":"24.1051846","display_name":"Rīga, Latvija","name":"Rīga","addresstype":"city",
          "category":"boundary","type":"administrative","place_rank":16,"importance":0.72,"osm_type":"relation","osm_id":13048688,
          "address":{"country_code":"lv","country":"Latvija","city":"Rīga"}}]
        """;

    public const long StreetOsmId = 4491949;

    private const string StreetSearchHit = """
        [{"lat":"59.3326","lon":"18.0649","display_name":"Drottninggatan, Norrmalm, Stockholm, Sverige","name":"Drottninggatan",
          "addresstype":"road","category":"highway","type":"pedestrian","place_rank":26,"importance":0.3,
          "osm_type":"way","osm_id":4491949,
          "address":{"road":"Drottninggatan","suburb":"Norrmalm","city":"Stockholm","country_code":"se","country":"Sverige"}}]
        """;

    private const string WeakSearchHit = """
        [{"lat":"55.4664","lon":"10.0522","display_name":"Riga, Middelfart Kommune, Danmark","name":"Riga",
          "addresstype":"hamlet","category":"place","type":"hamlet","place_rank":20,"importance":0.045,
          "osm_type":"node","osm_id":3118057996,"address":{"country_code":"dk","country":"Danmark","postcode":"5463"}}]
        """;

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

    public const long HouseOsmId = 8800110;

    private const string HouseReverseHit = """
        {"lat":"59.33262","lon":"18.06491","display_name":"10, Drottninggatan, Norrmalm, Stockholm, 111 51, Sverige","name":"",
         "addresstype":"building","category":"building","type":"yes","osm_type":"way","osm_id":8800110,
         "address":{"house_number":"10","road":"Drottninggatan","suburb":"Norrmalm","city":"Stockholm",
           "state":"Stockholms län","postcode":"111 51","country":"Sverige","country_code":"se"}}
        """;

    // What a regional instance actually returns for an out-of-coverage point: a country_osm_grid centroid, country-only.
    private const string CountryReverse = """
        {"lat":"36.5748441","lon":"139.2394179","display_name":"Japan","name":"Japan","addresstype":"country",
         "category":"boundary","type":"administrative","place_rank":4,"address":{"country":"Japan","country_code":"jp"}}
        """;
}
