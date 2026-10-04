using System.Net;
using Lupira.Testing.Postgres;
using Xunit;

namespace LupiraGeoApi.IntegrationTests;

/// <summary>Against the shared factory (ContactApi/CalendarApi unset): the sweep must refuse, not report orphans.</summary>
public sealed class OrphanSweepUnconfiguredTests(GeoApiTestFactory factory) : IntegrationTest(factory)
{
    [Fact]
    public async Task Unconfigured_reference_sources_fail_closed()
    {
        var api = Factory.ApiClient("alice@x.test");
        Assert.Equal(HttpStatusCode.BadRequest, (await api.GetAsync("/curation/orphans")).StatusCode);
    }
}
