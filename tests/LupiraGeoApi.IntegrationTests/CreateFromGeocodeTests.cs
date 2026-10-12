using System.Net;
using System.Net.Http.Json;
using Lupira.Testing.Postgres;
using LupiraGeoApi.Core.Domain;
using LupiraGeoApi.Core.Dtos.Geocoding;
using LupiraGeoApi.Core.Dtos.Places;
using Xunit;

namespace LupiraGeoApi.IntegrationTests;

/// <summary>POST /places/from-geocode — landing a user-picked geocode hit as a place: from the frozen forward cache
/// when the query is given, else by OSM lookup (regional, then public), else from the client's copy of the hit.</summary>
[Collection("geocoding")]
public sealed class CreateFromGeocodeTests(GeocodingFixture fx) : IAsyncLifetime
{
    const string Email = "alice@x.test";

    public Task InitializeAsync()
    {
        fx.Fallback.MultiHit = true;
        return fx.Factory.ResetAsync();
    }

    public Task DisposeAsync()
    {
        fx.Fallback.MultiHit = false;
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Picking_a_non_first_hit_creates_that_place_from_the_frozen_cache()
    {
        var api = fx.Factory.ApiClient(Email);

        var hits = (await api.GetFromJsonAsync<List<GeocodeResultDto>>("/geocode/forward?q=Shibuya%2C%20Tokyo"))!;
        Assert.Equal(2, hits.Count);
        Assert.Equal("way", hits[1].OsmType);
        Assert.Equal(654321, hits[1].OsmId);
        var (p0, f0) = (fx.Primary.SearchCalls, fx.Fallback.SearchCalls);

        var resp = await api.PostAsJsonAsync("/places/from-geocode",
            new CreatePlaceFromGeocodeRequest { Query = "Shibuya, Tokyo", OsmType = "way", OsmId = 654321, Name = "Shibuya Station" });
        resp.EnsureSuccessStatusCode();
        var created = (await resp.Content.ReadFromJsonAsync<ResolvePlaceResponse>())!;
        Assert.Equal(PlaceResolution.Geocoded, created.Resolution);
        Assert.Equal(35.6684, created.Latitude!.Value, 4);

        // Served from the frozen cache — no new outbound calls to either endpoint.
        Assert.Equal(p0, fx.Primary.SearchCalls);
        Assert.Equal(f0, fx.Fallback.SearchCalls);

        var got = (await api.GetFromJsonAsync<PlaceDto>($"/places/{created.PlaceId}"))!;
        Assert.Equal(PlaceSource.Geocoded, got.Source);
        Assert.Contains(got.ExternalIds, x => x.Scheme == ExternalScheme.Osm && x.Value == "way/654321");

        // Repeat pick of the same OSM object -> matched, same id, still no outbound calls.
        var again = (await (await api.PostAsJsonAsync("/places/from-geocode",
            new CreatePlaceFromGeocodeRequest { Query = "Shibuya, Tokyo", OsmType = "way", OsmId = 654321 }))
            .Content.ReadFromJsonAsync<ResolvePlaceResponse>())!;
        Assert.Equal(PlaceResolution.Matched, again.Resolution);
        Assert.Equal(created.PlaceId, again.PlaceId);
        Assert.Equal(p0, fx.Primary.SearchCalls);
        Assert.Equal(f0, fx.Fallback.SearchCalls);
    }

    [Fact]
    public async Task A_hit_not_among_the_querys_results_is_rejected()
    {
        var api = fx.Factory.ApiClient(Email);
        var resp = await api.PostAsJsonAsync("/places/from-geocode",
            new CreatePlaceFromGeocodeRequest { Query = "Shibuya, Tokyo", OsmType = "node", OsmId = 999999 });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Without_a_query_the_regional_lookup_resolves_the_hit_and_freezes_it()
    {
        var api = fx.Factory.ApiClient(Email);
        fx.Primary.ReturnResults = true;
        try
        {
            var (p0, f0) = (fx.Primary.LookupCalls, fx.Fallback.LookupCalls);
            var created = await CommitAsync(api, new CreatePlaceFromGeocodeRequest { OsmType = "W", OsmId = 654321 });
            Assert.Equal(PlaceResolution.Geocoded, created.Resolution);
            Assert.Equal("Shibuya Station", created.Name);
            Assert.Equal((p0 + 1, f0), (fx.Primary.LookupCalls, fx.Fallback.LookupCalls));

            var again = await CommitAsync(api, new CreatePlaceFromGeocodeRequest { OsmType = "way", OsmId = 654321 });
            Assert.Equal((PlaceResolution.Matched, created.PlaceId), (again.Resolution, again.PlaceId));
            Assert.Equal(p0 + 1, fx.Primary.LookupCalls);

            await using var session = fx.Factory.Store.QuerySession();
            Assert.NotNull(await session.LoadAsync<GeocodeCache>(GeocodeCache.LookupId("W654321")));
        }
        finally
        {
            fx.Primary.ReturnResults = false;
        }
    }

    [Fact]
    public async Task A_regional_lookup_miss_asks_the_public_fallback()
    {
        var api = fx.Factory.ApiClient(Email);
        var (p0, f0) = (fx.Primary.LookupCalls, fx.Fallback.LookupCalls);

        var created = await CommitAsync(api, new CreatePlaceFromGeocodeRequest { OsmType = "node", OsmId = 123456 });

        Assert.Equal(PlaceResolution.Geocoded, created.Resolution);
        Assert.Equal("Shibuya Crossing", created.Name);
        Assert.Equal((p0 + 1, f0 + 1), (fx.Primary.LookupCalls, fx.Fallback.LookupCalls));
        var got = (await api.GetFromJsonAsync<PlaceDto>($"/places/{created.PlaceId}"))!;
        Assert.Contains(got.ExternalIds, x => x.Scheme == ExternalScheme.Osm && x.Value == "node/123456");
    }

    [Fact]
    public async Task When_no_lookup_knows_the_hit_its_own_data_creates_an_approximate_place()
    {
        var api = fx.Factory.ApiClient(Email);
        var request = new CreatePlaceFromGeocodeRequest
        {
            OsmType = "node", OsmId = 24560001, Latitude = 59.3793, Longitude = 13.5036,
            Street = "Storgatan", HouseNumber = "5", Postcode = "652 24", Locality = "Karlstad", CountryCode = "SE",
        };

        var created = await CommitAsync(api, request);

        Assert.Equal(PlaceResolution.Geocoded, created.Resolution);
        Assert.Equal("Storgatan 5, Karlstad", created.Name);
        Assert.Equal((59.3793, 13.5036), (created.Latitude!.Value, created.Longitude!.Value));
        var got = (await api.GetFromJsonAsync<PlaceDto>($"/places/{created.PlaceId}"))!;
        Assert.DoesNotContain(got.ExternalIds, x => x.Scheme == ExternalScheme.Osm);
        Assert.Null(got.FormattedAddress);
        Assert.NotEmpty(got.Containment);

        var again = await CommitAsync(api, request);
        Assert.Equal((PlaceResolution.Matched, created.PlaceId), (again.Resolution, again.PlaceId));
    }

    private static async Task<ResolvePlaceResponse> CommitAsync(HttpClient api, CreatePlaceFromGeocodeRequest request)
    {
        var resp = await api.PostAsJsonAsync("/places/from-geocode", request);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<ResolvePlaceResponse>())!;
    }
}
