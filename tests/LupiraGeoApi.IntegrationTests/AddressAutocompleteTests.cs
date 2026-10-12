using System.Net.Http.Json;
using Lupira.Testing.Postgres;
using LupiraGeoApi.Core.Domain;
using LupiraGeoApi.Core.Dtos.Geocoding;
using Xunit;

namespace LupiraGeoApi.IntegrationTests;

/// <summary>GET /geocode/forward with address autocomplete configured: typeahead goes to it uncached, and a search
/// the geocoder cannot answer falls through to it.</summary>
[Collection("autocomplete")]
public sealed class AddressAutocompleteTests(AutocompleteFixture fx) : IAsyncLifetime
{
    const string Email = "alice@x.test";

    public Task InitializeAsync() => fx.Factory.ResetAsync();

    public Task DisposeAsync()
    {
        fx.Fallback.ReturnResults = true;
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Typeahead_goes_to_autocomplete_biased_and_uncached()
    {
        var api = fx.Factory.ApiClient(Email);
        var (p0, f0, a0) = (fx.Primary.SearchCalls, fx.Fallback.SearchCalls, fx.Photon.ApiCalls);

        var hits = (await api.GetFromJsonAsync<List<GeocodeResultDto>>(
            "/geocode/forward?q=Storg&intent=Typeahead&nearLat=59.38&nearLon=13.5&limit=3"))!;

        var hit = Assert.Single(hits);
        Assert.Equal(("Storgatan", "5", "652 24", "Karlstad"), (hit.Street, hit.HouseNumber, hit.Postcode, hit.Locality));
        Assert.Equal(("node", PhotonStub.HouseOsmId), (hit.OsmType, hit.OsmId!.Value));
        Assert.False(hit.IsArea);
        Assert.Contains("lat=59.38&lon=13.5", fx.Photon.LastQuery);
        Assert.Contains("limit=3", fx.Photon.LastQuery);
        Assert.Equal((p0, f0, a0 + 1), (fx.Primary.SearchCalls, fx.Fallback.SearchCalls, fx.Photon.ApiCalls));

        await using (var session = fx.Factory.Store.QuerySession())
            Assert.Null(await session.LoadAsync<GeocodeCache>(GeocodeCache.ForwardId("Storg")));

        await api.GetFromJsonAsync<List<GeocodeResultDto>>("/geocode/forward?q=Storg&intent=Typeahead");
        Assert.Equal(a0 + 2, fx.Photon.ApiCalls);
    }

    [Fact]
    public async Task Structured_typeahead_uses_the_structured_endpoint()
    {
        var api = fx.Factory.ApiClient(Email);
        var s0 = fx.Photon.StructuredCalls;

        var hits = await api.GetFromJsonAsync<List<GeocodeResultDto>>(
            "/geocode/forward?street=Storgatan%205&locality=Karlstad&countryCode=se&intent=Typeahead");

        Assert.Single(hits!);
        Assert.Equal(s0 + 1, fx.Photon.StructuredCalls);
        Assert.Contains("street=Storgatan&housenumber=5&city=Karlstad&countrycode=SE", fx.Photon.LastQuery);
    }

    [Fact]
    public async Task Search_falls_through_to_autocomplete_only_when_the_geocoder_has_nothing()
    {
        var api = fx.Factory.ApiClient(Email);
        var a0 = fx.Photon.ApiCalls;

        var resolved = (await api.GetFromJsonAsync<List<GeocodeResultDto>>("/geocode/forward?q=Shibuya%20Crossing"))!;
        Assert.Equal("Shibuya", resolved[0].Locality);
        Assert.Equal(a0, fx.Photon.ApiCalls);

        fx.Fallback.ReturnResults = false;
        var suggested = (await api.GetFromJsonAsync<List<GeocodeResultDto>>("/geocode/forward?q=Storgatan%205%2C%20Karlstad"))!;
        Assert.Equal("Karlstad", Assert.Single(suggested).Locality);
        Assert.Equal(a0 + 1, fx.Photon.ApiCalls);
    }
}
