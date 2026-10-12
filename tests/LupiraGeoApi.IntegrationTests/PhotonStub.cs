using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace LupiraGeoApi.IntegrationTests;

/// <summary>In-process address-autocomplete double (Photon's <c>/api</c> + <c>/structured</c>) on a random loopback
/// port. Counts requests and keeps the latest query string.</summary>
public sealed class PhotonStub : IAsyncDisposable
{
    private readonly WebApplication _app;
    private int _apiCalls, _structuredCalls;

    public string BaseUrl { get; private set; } = "";
    public int ApiCalls => Volatile.Read(ref _apiCalls);
    public int StructuredCalls => Volatile.Read(ref _structuredCalls);
    public string LastQuery { get; private set; } = "";

    public bool ReturnResults { get; set; } = true;

    public const long HouseOsmId = 24560001;

    private PhotonStub(WebApplication app) => _app = app;

    public static async Task<PhotonStub> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var app = builder.Build();
        var stub = new PhotonStub(app);

        app.MapGet("/api", (HttpRequest req) =>
        {
            Interlocked.Increment(ref stub._apiCalls);
            stub.LastQuery = req.QueryString.Value ?? "";
            return Results.Content(stub.ReturnResults ? Features : Empty, "application/json");
        });
        app.MapGet("/structured", (HttpRequest req) =>
        {
            Interlocked.Increment(ref stub._structuredCalls);
            stub.LastQuery = req.QueryString.Value ?? "";
            return Results.Content(stub.ReturnResults ? Features : Empty, "application/json");
        });

        await app.StartAsync();
        stub.BaseUrl = app.Urls.First();
        return stub;
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    private const string Empty = """{"type":"FeatureCollection","features":[]}""";

    private const string Features = """
        {"type":"FeatureCollection","features":[
          {"type":"Feature","geometry":{"type":"Point","coordinates":[13.5036,59.3793]},
           "properties":{"osm_type":"N","osm_id":24560001,"osm_key":"place","osm_value":"house","type":"house",
             "housenumber":"5","street":"Storgatan","postcode":"652 24","city":"Karlstad","state":"Värmlands län",
             "country":"Sverige","countrycode":"SE"}}]}
        """;
}
