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
