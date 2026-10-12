using System.Net;
using System.Net.Http.Json;
using Lupira.Testing.Postgres;
using LupiraGeoApi.Core.Domain;
using LupiraGeoApi.Core.Dtos.Geocoding;
using Xunit;

namespace LupiraGeoApi.IntegrationTests;

/// <summary>GET /geocode/forward and /geocode/reverse without address autocomplete configured: structured search,
/// typeahead kept off the public endpoint, and the building-precision reverse cache.</summary>
[Collection("geocoding")]
public sealed class ForwardGeocodeRoutingTests(GeocodingFixture fx) : IAsyncLifetime
{
    const string Email = "alice@x.test";

    public Task InitializeAsync() => fx.Factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Structured_fields_are_forwarded_upstream_and_cached_under_their_own_key()
    {
        var api = fx.Factory.ApiClient(Email);

        var hits = await api.GetFromJsonAsync<List<GeocodeResultDto>>(
            "/geocode/forward?street=Drottninggatan%2010&postcode=111%2051&locality=Stockholm&countryCode=SE");

        Assert.NotEmpty(hits!);
        var sent = fx.Primary.LastSearchQuery;
        Assert.Contains("street=Drottninggatan%2010", sent);
        Assert.Contains("city=Stockholm", sent);
        Assert.Contains("postalcode=111%2051", sent);
        Assert.Contains("countrycodes=se", sent);
        Assert.DoesNotContain("&q=", sent);

        await using var session = fx.Factory.Store.QuerySession();
        Assert.NotNull(await session.LoadAsync<GeocodeCache>(
            GeocodeCache.StructuredForwardId("Drottninggatan 10", "111 51", "Stockholm", "SE")));
    }

    [Theory]
    [InlineData("postcode=11151&countryCode=SE")]
    [InlineData("q=Storgatan&street=Storgatan")]
    [InlineData("intent=Typeahead")]
    [InlineData("q=x&nearLat=59")]
    public async Task Invalid_forward_queries_are_rejected(string query)
    {
        var resp = await fx.Factory.ApiClient(Email).GetAsync($"/geocode/forward?{query}");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Typeahead_without_autocomplete_asks_only_the_regional_instance_and_caches_nothing()
    {
        var api = fx.Factory.ApiClient(Email);
        var (p0, f0) = (fx.Primary.SearchCalls, fx.Fallback.SearchCalls);

        var hits = await api.GetFromJsonAsync<List<GeocodeResultDto>>("/geocode/forward?q=Shibuya&intent=Typeahead");

        Assert.Empty(hits!);
        Assert.Equal((p0 + 1, f0), (fx.Primary.SearchCalls, fx.Fallback.SearchCalls));
        await using var session = fx.Factory.Store.QuerySession();
        Assert.Null(await session.LoadAsync<GeocodeCache>(GeocodeCache.ForwardId("Shibuya")));
    }

    [Fact]
    public async Task Building_precision_reverse_uses_the_fine_grid_and_building_zoom()
    {
        var api = fx.Factory.ApiClient(Email);

        var hit = await api.GetFromJsonAsync<GeocodeResultDto>("/geocode/reverse?lat=59.33261&lon=18.06493&precision=Building");

        Assert.NotNull(hit);
        Assert.Contains("zoom=18", fx.Fallback.LastReverseQuery);
        Assert.Contains("lat=59.3326&lon=18.0649", fx.Fallback.LastReverseQuery);
        await using var session = fx.Factory.Store.QuerySession();
        Assert.NotNull(await session.LoadAsync<GeocodeCache>(GeocodeCache.FineReverseId(59.33261, 18.06493)));
        Assert.Null(await session.LoadAsync<GeocodeCache>(GeocodeCache.ReverseId(59.33261, 18.06493)));
    }
}
