using Xunit;

namespace LupiraGeoApi.IntegrationTests;

/// <summary>Regional (missing) and public (hitting) Nominatim stubs plus an address-autocomplete stub. Own collection:
/// with autocomplete configured, an empty search falls through to it.</summary>
public sealed class AutocompleteFixture : IAsyncLifetime
{
    public NominatimStub Primary { get; private set; } = null!;
    public NominatimStub Fallback { get; private set; } = null!;
    public PhotonStub Photon { get; private set; } = null!;
    public GeoApiTestFactory Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Primary = await NominatimStub.StartAsync(returnResults: false);
        Fallback = await NominatimStub.StartAsync(returnResults: true);
        Photon = await PhotonStub.StartAsync();
        Factory = new GeoApiTestFactory();
        Factory.ExtraConfig["Nominatim:BaseUrl"] = Primary.BaseUrl;
        Factory.ExtraConfig["Nominatim:FallbackBaseUrl"] = Fallback.BaseUrl;
        Factory.ExtraConfig["Photon:BaseUrl"] = Photon.BaseUrl;
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await Primary.DisposeAsync();
        await Fallback.DisposeAsync();
        await Photon.DisposeAsync();
    }
}
