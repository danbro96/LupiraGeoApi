using System.Net.Http.Json;
using Lupira.Testing.Postgres;
using LupiraGeoApi.Core.Domain;
using LupiraGeoApi.Core.Dtos.Places;
using Xunit;

namespace LupiraGeoApi.IntegrationTests;

/// <summary>POST /places/from-pin — a dropped pin lands at its exact coordinates, named and contained by a
/// building-precision reverse geocode, and matches a similar name within 25 m.</summary>
[Collection("geocoding")]
public sealed class PlaceFromPinTests(GeocodingFixture fx) : IAsyncLifetime
{
    const string Email = "alice@x.test";

    public Task InitializeAsync()
    {
        fx.Fallback.HouseReverse = true;
        return fx.Factory.ResetAsync();
    }

    public Task DisposeAsync()
    {
        fx.Fallback.HouseReverse = false;
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Pin_keeps_exact_coordinates_and_takes_address_and_containment_from_reverse()
    {
        var api = fx.Factory.ApiClient(Email);

        var created = await PinAsync(api, new CreatePlaceFromPinRequest { Latitude = 59.33261, Longitude = 18.06493 });

        Assert.Equal(PlaceResolution.Created, created.Resolution);
        Assert.Equal("Drottninggatan 10, Stockholm", created.Name);
        Assert.Equal((59.33261, 18.06493), (created.Latitude!.Value, created.Longitude!.Value));
        Assert.Contains("zoom=18", fx.Fallback.LastReverseQuery);
        Assert.Contains("lat=59.3326&", fx.Fallback.LastReverseQuery);

        var place = (await api.GetFromJsonAsync<PlaceDto>($"/places/{created.PlaceId}"))!;
        Assert.Equal(PlaceSource.User, place.Source);
        Assert.StartsWith("10, Drottninggatan", place.FormattedAddress);
        Assert.Contains(place.Containment, a => a.Name == "Stockholm");
    }

    [Fact]
    public async Task A_second_pin_nearby_matches_the_first()
    {
        var api = fx.Factory.ApiClient(Email);
        var first = await PinAsync(api, new CreatePlaceFromPinRequest { Latitude = 59.33261, Longitude = 18.06493 });

        var second = await PinAsync(api, new CreatePlaceFromPinRequest { Latitude = 59.33270, Longitude = 18.06493 });

        Assert.Equal((PlaceResolution.Matched, first.PlaceId), (second.Resolution, second.PlaceId));
    }

    [Fact]
    public async Task The_same_name_far_apart_is_two_places()
    {
        var api = fx.Factory.ApiClient(Email);
        var here = await PinAsync(api, new CreatePlaceFromPinRequest { Name = "Stugan", Latitude = 60.1, Longitude = 13.0 });

        var there = await PinAsync(api, new CreatePlaceFromPinRequest { Name = "Stugan", Latitude = 60.2, Longitude = 13.1, Category = PlaceCategory.Landmark });

        Assert.Equal((PlaceResolution.Created, PlaceResolution.Created), (here.Resolution, there.Resolution));
        Assert.NotEqual(here.PlaceId, there.PlaceId);
        Assert.Equal("Stugan", there.Name);
    }

    internal static async Task<ResolvePlaceResponse> PinAsync(HttpClient api, CreatePlaceFromPinRequest request)
    {
        var resp = await api.PostAsJsonAsync("/places/from-pin", request);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<ResolvePlaceResponse>())!;
    }
}
