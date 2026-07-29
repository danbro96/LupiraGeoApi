using LupiraGeoApi.Domain;
using LupiraGeoApi.Dtos.Places;
using System.Net.Http.Json;
using Xunit;

namespace LupiraGeoApi.IntegrationTests;

/// <summary>A primary Nominatim that always 503s, with no fallback configured: forward geocoding is unreachable.
/// Own collection so the failing endpoint never leaks into other tests.</summary>
public sealed class GeocoderDownFixture : IAsyncLifetime
{
    public NominatimStub Primary { get; private set; } = null!;
    public GeoApiTestFactory Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Primary = await NominatimStub.StartAsync(returnResults: false, failStatus: 503);
        Factory = new GeoApiTestFactory();
        Factory.ExtraConfig["Nominatim:BaseUrl"] = Primary.BaseUrl;
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await Primary.DisposeAsync();
    }
}

[CollectionDefinition("geocoder-down")]
public sealed class GeocoderDownCollection : ICollectionFixture<GeocoderDownFixture>;

/// <summary>The core of the resolve hardening: a transient geocoder outage must never be mistaken for "not found"
/// and must never mint a permanent coordinate-less stub — the caller gets a retryable status instead.</summary>
[Collection("geocoder-down")]
public sealed class ResolveUnavailableTests(GeocoderDownFixture fx) : IAsyncLifetime
{
    const string Email = "alice@x.test";

    public Task InitializeAsync() => fx.Factory.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Resolve_when_geocoder_unreachable_creates_nothing_and_is_retryable()
    {
        var api = fx.Factory.ApiClient(Email);
        var before = fx.Primary.SearchCalls;

        var resp = await api.PostAsJsonAsync("/places/resolve", new ResolvePlaceRequest { Text = "Somewhere Unreachable" });
        resp.EnsureSuccessStatusCode();
        var resolved = (await resp.Content.ReadFromJsonAsync<ResolvePlaceResponse>())!;

        Assert.Equal(PlaceResolution.GeocodeUnavailable, resolved.Resolution);
        Assert.Null(resolved.PlaceId);                                   // nothing persisted
        Assert.Null(resolved.Latitude);
        Assert.True(fx.Primary.SearchCalls > before + 1, "the transient 503 should be retried");

        // No poison stub left behind: the text resolves to nothing and search finds nothing.
        var hits = await api.GetFromJsonAsync<List<PlaceDto>>("/places?q=Unreachable");
        Assert.Empty(hits!);
    }

    [Fact]
    public async Task Batch_resolve_survives_a_per_item_outage()
    {
        var api = fx.Factory.ApiClient(Email);
        var resp = await api.PostAsJsonAsync("/places/resolve:batch",
            new ResolvePlacesBatchRequest { Texts = ["Alpha", "Beta"] });
        resp.EnsureSuccessStatusCode();                                  // batch completes, does not abort
        var resolved = (await resp.Content.ReadFromJsonAsync<List<ResolvePlaceResponse>>())!;

        Assert.Equal(2, resolved.Count);
        Assert.All(resolved, r => Assert.Equal(PlaceResolution.GeocodeUnavailable, r.Resolution));
        Assert.All(resolved, r => Assert.Null(r.PlaceId));
    }
}
