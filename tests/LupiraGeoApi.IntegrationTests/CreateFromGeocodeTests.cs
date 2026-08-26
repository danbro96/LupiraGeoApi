using System.Net;
using System.Net.Http.Json;
using LupiraGeoApi.Core.Domain;
using LupiraGeoApi.Core.Dtos.Geocoding;
using LupiraGeoApi.Core.Dtos.Places;
using Xunit;

namespace LupiraGeoApi.IntegrationTests;

/// <summary>POST /places/from-geocode — landing a user-picked geocode hit as a place: served entirely from the
/// frozen forward cache (zero extra Nominatim calls), deduped by OSM identity on repeat.</summary>
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

        var hits = (await api.GetFromJsonAsync<List<GeocodeResultDto>>("/geocode/forward?q=Shibuya"))!;
        Assert.Equal(2, hits.Count);
        Assert.Equal("way", hits[1].OsmType);
        Assert.Equal(654321, hits[1].OsmId);
        var (p0, f0) = (fx.Primary.SearchCalls, fx.Fallback.SearchCalls);

        var resp = await api.PostAsJsonAsync("/places/from-geocode",
            new CreatePlaceFromGeocodeRequest { Query = "Shibuya", OsmType = "way", OsmId = 654321, Name = "Shibuya Station" });
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
            new CreatePlaceFromGeocodeRequest { Query = "Shibuya", OsmType = "way", OsmId = 654321 }))
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
            new CreatePlaceFromGeocodeRequest { Query = "Shibuya", OsmType = "node", OsmId = 999999 });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }
}
