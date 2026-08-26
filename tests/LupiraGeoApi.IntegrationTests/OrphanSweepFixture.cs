using Xunit;

namespace LupiraGeoApi.IntegrationTests;

/// <summary>Own collection: the factory points ContactApi/CalendarApi at a <see cref="PlaceRefsStub"/>.</summary>
public sealed class OrphanSweepFixture : IAsyncLifetime
{
    public PlaceRefsStub Refs { get; private set; } = null!;
    public GeoApiTestFactory Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Refs = await PlaceRefsStub.StartAsync();
        Factory = new GeoApiTestFactory();
        Factory.ExtraConfig["ContactApi:BaseUrl"] = Refs.BaseUrl;
        Factory.ExtraConfig["ContactApi:DevUser"] = "geo-svc@x.test";
        Factory.ExtraConfig["CalendarApi:BaseUrl"] = Refs.BaseUrl;
        Factory.ExtraConfig["CalendarApi:DevUser"] = "geo-svc@x.test";
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await Refs.DisposeAsync();
    }
}
