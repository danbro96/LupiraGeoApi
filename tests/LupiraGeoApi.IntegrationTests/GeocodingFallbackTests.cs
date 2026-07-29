using LupiraGeoApi.Domain;
using LupiraGeoApi.Dtos.Geocoding;
using LupiraGeoApi.Dtos.Places;
using System.Net.Http.Json;
using Xunit;

namespace LupiraGeoApi.IntegrationTests;

/// <summary>Two Nominatim stubs: a "regional" primary that misses everything and a "public" fallback that hits.
/// Own collection — the shared factory keeps Nominatim unset.</summary>
public sealed class GeocodingFixture : IAsyncLifetime
{
    public NominatimStub Primary { get; private set; } = null!;
    public NominatimStub Fallback { get; private set; } = null!;
    public GeoApiTestFactory Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Primary = await NominatimStub.StartAsync(returnResults: false);
        Fallback = await NominatimStub.StartAsync(returnResults: true);
        Factory = new GeoApiTestFactory();
        Factory.ExtraConfig["Nominatim:BaseUrl"] = Primary.BaseUrl;
        Factory.ExtraConfig["Nominatim:FallbackBaseUrl"] = Fallback.BaseUrl;
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await Primary.DisposeAsync();
        await Fallback.DisposeAsync();
    }
}

[CollectionDefinition("geocoding")]
public sealed class GeocodingCollection : ICollectionFixture<GeocodingFixture>;

[Collection("geocoding")]
public sealed class GeocodingFallbackTests(GeocodingFixture fx) : IAsyncLifetime
{
    const string Email = "alice@x.test";

    public Task InitializeAsync() => fx.Factory.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Forward_falls_back_to_the_public_endpoint_and_freezes()
    {
        var api = fx.Factory.ApiClient(Email);
        var (p0, f0) = (fx.Primary.SearchCalls, fx.Fallback.SearchCalls);

        var resp = await api.PostAsJsonAsync("/places/resolve", new ResolvePlaceRequest { Text = "Shibuya Crossing" });
        resp.EnsureSuccessStatusCode();
        var resolved = (await resp.Content.ReadFromJsonAsync<ResolvePlaceResponse>())!;
        Assert.Equal(35.6595, resolved.Latitude!.Value, 4);
        Assert.Equal(p0 + 1, fx.Primary.SearchCalls);   // primary tried (empty)…
        Assert.Equal(f0 + 1, fx.Fallback.SearchCalls);  // …fallback answered

        var got = (await api.GetFromJsonAsync<PlaceDto>($"/places/{resolved.PlaceId}"))!;
        Assert.Equal(PlaceSource.Geocoded, got.Source);
        Assert.Contains(got.Containment, a => a.Name == "Japan");

        // The OSM external id from the fallback hit reconciles.
        var byExt = (await api.GetFromJsonAsync<PlaceDto>("/places/by-external/Osm/node/123456"))!;
        Assert.Equal(resolved.PlaceId, byExt.Id);

        // Frozen: the same query serves from the cache — no new outbound calls to either endpoint.
        var fwd = (await api.GetFromJsonAsync<List<GeocodeResultDto>>("/geocode/forward?q=Shibuya%20Crossing"))!;
        Assert.Single(fwd);
        Assert.Equal(p0 + 1, fx.Primary.SearchCalls);
        Assert.Equal(f0 + 1, fx.Fallback.SearchCalls);
    }

    [Fact]
    public async Task Resolve_dedupes_a_second_text_form_of_the_same_osm_object()
    {
        var api = fx.Factory.ApiClient(Email);

        var first = (await (await api.PostAsJsonAsync("/places/resolve", new ResolvePlaceRequest { Text = "Shibuya Crossing" }))
            .Content.ReadFromJsonAsync<ResolvePlaceResponse>())!;
        Assert.Equal(PlaceResolution.Geocoded, first.Resolution);

        // A different free-text form (comma-qualified) that geocodes to the SAME OSM object. Name+proximity dedup
        // misses it because the canonical name differs; without OSM-id dedup the insert would collide on the unique
        // (Scheme, Value) external-id index and 500. It must reconcile to the existing place as Matched.
        var resp = await api.PostAsJsonAsync("/places/resolve",
            new ResolvePlaceRequest { Text = "Shibuya Scramble Crossing, Tokyo, Japan" });
        resp.EnsureSuccessStatusCode();
        var second = (await resp.Content.ReadFromJsonAsync<ResolvePlaceResponse>())!;

        Assert.Equal(PlaceResolution.Matched, second.Resolution);
        Assert.Equal(first.PlaceId, second.PlaceId);
    }

    [Fact]
    public async Task Regeocode_heals_a_coordinate_less_place()
    {
        var api = fx.Factory.ApiClient(Email);
        // A user place with no coordinates — what resolve leaves behind on a geocoder miss.
        var created = (await (await api.PostAsJsonAsync("/places", new CreatePlaceRequest { Name = "Shibuya Crossing" }))
            .Content.ReadFromJsonAsync<PlaceDto>())!;
        Assert.Null(created.Latitude);
        Assert.Equal(PlaceSource.User, created.Source);

        var resp = await api.PostAsync($"/places/{created.Id}/regeocode", null);
        resp.EnsureSuccessStatusCode();
        var healed = (await resp.Content.ReadFromJsonAsync<PlaceDto>())!;

        Assert.Equal(PlaceSource.Geocoded, healed.Source);
        Assert.Equal(35.6595, healed.Latitude!.Value, 4);
        Assert.Contains(healed.Containment, a => a.Name == "Japan");
        Assert.Contains(healed.ExternalIds, x => x.Scheme == ExternalScheme.Osm && x.Value == "node/123456");
    }

    [Fact]
    public async Task Reverse_falls_back_when_the_regional_instance_cannot_geocode()
    {
        var api = fx.Factory.ApiClient(Email);
        var (p0, f0) = (fx.Primary.ReverseCalls, fx.Fallback.ReverseCalls);

        var hit = (await api.GetFromJsonAsync<GeocodeResultDto>("/geocode/reverse?lat=35.6595&lon=139.7005"))!;
        Assert.Contains("Shibuya", hit.DisplayName);
        Assert.Equal(p0 + 1, fx.Primary.ReverseCalls);
        Assert.Equal(f0 + 1, fx.Fallback.ReverseCalls);

        var again = (await api.GetFromJsonAsync<GeocodeResultDto>("/geocode/reverse?lat=35.6595&lon=139.7005"))!;
        Assert.Contains("Shibuya", again.DisplayName);
        Assert.Equal(p0 + 1, fx.Primary.ReverseCalls);  // cache hit — no new calls
        Assert.Equal(f0 + 1, fx.Fallback.ReverseCalls);
    }
}
