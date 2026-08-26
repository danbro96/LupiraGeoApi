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
