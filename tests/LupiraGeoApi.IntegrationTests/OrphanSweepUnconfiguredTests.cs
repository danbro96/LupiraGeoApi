using System.Net;
using System.Net.Http.Json;
using LupiraGeoApi.Core.Domain;
using LupiraGeoApi.Core.Dtos.Curation;
using LupiraGeoApi.Core.Dtos.Places;
using LupiraGeoApi.Core.Dtos.SavedPlaces;
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
