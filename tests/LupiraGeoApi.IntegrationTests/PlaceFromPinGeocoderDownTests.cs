using Lupira.Testing.Postgres;
using LupiraGeoApi.Core.Domain;
using LupiraGeoApi.Core.Dtos.Places;
using Xunit;

namespace LupiraGeoApi.IntegrationTests;

[Collection("geocoder-down")]
public sealed class PlaceFromPinGeocoderDownTests(GeocoderDownFixture fx) : IAsyncLifetime
{
    public Task InitializeAsync() => fx.Factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_pin_is_created_from_its_coordinates_when_the_geocoder_is_down()
    {
        var api = fx.Factory.ApiClient("alice@x.test");

        var created = await PlaceFromPinTests.PinAsync(api, new CreatePlaceFromPinRequest { Latitude = 59.3326, Longitude = 18.0649 });

        Assert.Equal(PlaceResolution.Created, created.Resolution);
        Assert.Equal("59.33260, 18.06490", created.Name);
        Assert.Equal((59.3326, 18.0649), (created.Latitude!.Value, created.Longitude!.Value));
    }
}
