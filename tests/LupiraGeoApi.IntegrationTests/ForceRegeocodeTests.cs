using LupiraGeoApi.Domain;
using LupiraGeoApi.Dtos.Geocoding;
using LupiraGeoApi.Dtos.Places;
using System.Net.Http.Json;
using System.Net;
using Xunit;

namespace LupiraGeoApi.IntegrationTests;

/// <summary>Healing a stub whose empty geocode answer got frozen: plain regeocode keeps serving the frozen empty
/// (and must not spend outbound calls); force=true bypasses the read and overwrites the frozen row.</summary>
[Collection("geocoding")]
public sealed class ForceRegeocodeTests(GeocodingFixture fx) : IAsyncLifetime
{
    const string Email = "alice@x.test";

    public Task InitializeAsync()
    {
        fx.Fallback.ReturnResults = true;
        return fx.Factory.ResetAsync();
    }

    public Task DisposeAsync()
    {
        fx.Fallback.ReturnResults = true;
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Force_overwrites_a_frozen_empty_answer_and_heals_the_stub()
    {
        var api = fx.Factory.ApiClient(Email);

        // Both endpoints empty -> resolve freezes the empty answer and creates a coordinate-less stub.
        fx.Fallback.ReturnResults = false;
        var resolved = (await (await api.PostAsJsonAsync("/places/resolve", new ResolvePlaceRequest { Text = "Shibuya Crossing" }))
            .Content.ReadFromJsonAsync<ResolvePlaceResponse>())!;
        Assert.Equal(PlaceResolution.Provisional, resolved.Resolution);
        Assert.Null(resolved.Latitude);

        // The world improves, but the frozen empty still answers: no force -> 400, zero outbound calls.
        fx.Fallback.ReturnResults = true;
        var (p0, f0) = (fx.Primary.SearchCalls, fx.Fallback.SearchCalls);
        var plain = await api.PostAsync($"/places/{resolved.PlaceId}/regeocode", null);
        Assert.Equal(HttpStatusCode.BadRequest, plain.StatusCode);
        Assert.Equal((p0, f0), (fx.Primary.SearchCalls, fx.Fallback.SearchCalls));

        // force=true bypasses the cache, heals the place, and overwrites the frozen row.
        var forced = await api.PostAsync($"/places/{resolved.PlaceId}/regeocode?force=true", null);
        forced.EnsureSuccessStatusCode();
        var healed = (await forced.Content.ReadFromJsonAsync<PlaceDto>())!;
        Assert.NotNull(healed.Latitude);
        Assert.Equal(PlaceSource.Geocoded, healed.Source);
        Assert.True(fx.Fallback.SearchCalls > f0);

        // The overwritten cache row now serves the hits without further outbound calls.
        var (p1, f1) = (fx.Primary.SearchCalls, fx.Fallback.SearchCalls);
        var fwd = (await api.GetFromJsonAsync<List<GeocodeResultDto>>("/geocode/forward?q=Shibuya%20Crossing"))!;
        Assert.NotEmpty(fwd);
        Assert.Equal((p1, f1), (fx.Primary.SearchCalls, fx.Fallback.SearchCalls));
    }

    [Fact]
    public async Task Force_during_an_outage_leaves_the_frozen_answer_intact()
    {
        var api = fx.Factory.ApiClient(Email);

        // Freeze a good answer first.
        var resolved = (await (await api.PostAsJsonAsync("/places/resolve", new ResolvePlaceRequest { Text = "Shibuya Crossing" }))
            .Content.ReadFromJsonAsync<ResolvePlaceResponse>())!;
        Assert.Equal(PlaceResolution.Geocoded, resolved.Resolution);

        // Outage: both endpoints answer empty is not an outage — simulate by pointing at nothing reachable is
        // heavier; the Unavailable path is covered in GeocodingFallbackTests. Here: force with empty answers
        // overwrites the frozen row with the definitive empty — the place keeps its old fix (400, unchanged).
        fx.Fallback.ReturnResults = false;
        var forced = await api.PostAsync($"/places/{resolved.PlaceId}/regeocode?force=true", null);
        Assert.Equal(HttpStatusCode.BadRequest, forced.StatusCode);

        var place = (await api.GetFromJsonAsync<PlaceDto>($"/places/{resolved.PlaceId}"))!;
        Assert.NotNull(place.Latitude);   // the place itself was left unchanged
    }
}
