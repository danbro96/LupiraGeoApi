using System.Net;
using System.Net.Http.Json;
using Lupira.Testing.Postgres;
using LupiraGeoApi.Core.Domain;
using LupiraGeoApi.Core.Dtos.Places;
using Xunit;

namespace LupiraGeoApi.IntegrationTests;

/// <summary>Numbered addresses whose geocode only finds the street: each number stays its own place, and none of
/// them claims the street's OSM id.</summary>
[Collection("geocoding")]
public sealed class StreetLevelGeocodeTests(GeocodingFixture fx) : IAsyncLifetime
{
    const string Email = "alice@x.test";
    const string StreetOsm = "way/4491949";

    public Task InitializeAsync()
    {
        fx.Fallback.StreetHit = true;
        return fx.Factory.ResetAsync();
    }

    public Task DisposeAsync()
    {
        fx.Fallback.StreetHit = false;
        return Task.CompletedTask;
    }

    private static async Task<ResolvePlaceResponse> ResolveAsync(HttpClient api, string text)
    {
        var resp = await api.PostAsJsonAsync("/places/resolve", new ResolvePlaceRequest { Text = text });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<ResolvePlaceResponse>())!;
    }

    [Fact]
    public async Task Different_numbers_on_one_street_resolve_to_different_places()
    {
        var api = fx.Factory.ApiClient(Email);

        var ten = await ResolveAsync(api, "Drottninggatan 10, Stockholm");
        Assert.Equal(PlaceResolution.Geocoded, ten.Resolution);
        Assert.Equal(59.3326, ten.Latitude!.Value, 4);

        var twentyTwo = await ResolveAsync(api, "Drottninggatan 22, Stockholm");
        Assert.Equal(PlaceResolution.Geocoded, twentyTwo.Resolution);
        Assert.NotEqual(ten.PlaceId, twentyTwo.PlaceId);

        var again = await ResolveAsync(api, "Drottninggatan 22");
        Assert.Equal(PlaceResolution.Matched, again.Resolution);
        Assert.Equal(twentyTwo.PlaceId, again.PlaceId);

        var place = (await api.GetFromJsonAsync<PlaceDto>($"/places/{ten.PlaceId}"))!;
        Assert.Equal("Drottninggatan 10, Stockholm", place.Name);
        Assert.Equal(PlaceKind.Poi, place.Kind);
        Assert.Null(place.FormattedAddress);
        Assert.DoesNotContain(place.ExternalIds, x => x.Scheme == ExternalScheme.Osm);
    }

    [Fact]
    public async Task The_street_itself_keeps_its_osm_id_and_does_not_absorb_a_numbered_address()
    {
        var api = fx.Factory.ApiClient(Email);

        var house = await ResolveAsync(api, "Drottninggatan 10, Stockholm");
        var street = await ResolveAsync(api, "Drottninggatan, Stockholm");
        Assert.Equal(PlaceResolution.Geocoded, street.Resolution);
        Assert.NotEqual(house.PlaceId, street.PlaceId);

        var byOsm = (await api.GetFromJsonAsync<PlaceDto>($"/places/by-external/Osm/{StreetOsm}"))!;
        Assert.Equal(street.PlaceId, byOsm.Id);

        var nextHouse = await ResolveAsync(api, "Drottninggatan 22, Stockholm");
        Assert.Equal(PlaceResolution.Geocoded, nextHouse.Resolution);
        Assert.NotEqual(street.PlaceId, nextHouse.PlaceId);
    }

    [Fact]
    public async Task Regeocoding_a_numbered_address_to_its_street_sets_coordinates_only()
    {
        var api = fx.Factory.ApiClient(Email);
        var created = await api.PostAsJsonAsync("/places", new CreatePlaceRequest { Name = "Drottninggatan 30, Stockholm" });
        created.EnsureSuccessStatusCode();
        var stub = (await created.Content.ReadFromJsonAsync<PlaceDto>())!;

        var resp = await api.PostAsync($"/places/{stub.Id}/regeocode", null);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var healed = (await resp.Content.ReadFromJsonAsync<PlaceDto>())!;
        Assert.Equal(59.3326, healed.Latitude!.Value, 4);
        Assert.Null(healed.FormattedAddress);
        Assert.DoesNotContain(healed.ExternalIds, x => x.Scheme == ExternalScheme.Osm);
    }
}
