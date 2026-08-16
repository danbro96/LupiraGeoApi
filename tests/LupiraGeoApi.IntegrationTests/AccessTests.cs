using System.Net;
using Xunit;

namespace LupiraGeoApi.IntegrationTests;

/// <summary>The whole REST surface is authenticated: no anonymous reads or writes.</summary>
public sealed class AccessTests(GeoApiTestFactory factory) : IntegrationTest(factory)
{
    [Theory]
    [InlineData("/places")]
    [InlineData("/places/suggest?q=xy")]
    [InlineData("/places/by-external/Osm/node/1")]
    [InlineData("/places/11111111-1111-1111-1111-111111111111/history")]
    [InlineData("/geocode/forward?q=x")]
    [InlineData("/admin-areas")]
    [InlineData("/me")]
    [InlineData("/me/places")]
    [InlineData("/curation/orphans")]
    public async Task Anonymous_requests_are_rejected(string path)
    {
        var anon = Factory.AnonymousClient();
        var resp = await anon.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Tunnelled_curation_requests_are_hidden()
    {
        var api = Factory.ApiClient("alice@x.test");
        using var req = new HttpRequestMessage(HttpMethod.Get, "/curation/orphans");
        req.Headers.Add("CF-Ray", "8a1b2c3d4e5f6789-ARN");
        Assert.Equal(HttpStatusCode.NotFound, (await api.SendAsync(req)).StatusCode);
    }
}
