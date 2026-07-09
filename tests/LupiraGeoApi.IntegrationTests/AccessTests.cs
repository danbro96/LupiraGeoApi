using System.Net;
using Xunit;

namespace LupiraGeoApi.IntegrationTests;

/// <summary>The whole REST surface is authenticated: no anonymous reads or writes.</summary>
public sealed class AccessTests(GeoApiTestFactory factory) : IntegrationTest(factory)
{
    [Theory]
    [InlineData("/places")]
    [InlineData("/geocode/forward?q=x")]
    [InlineData("/admin-areas")]
    [InlineData("/me")]
    [InlineData("/me/places")]
    public async Task Anonymous_requests_are_rejected(string path)
    {
        var anon = Factory.AnonymousClient();
        var resp = await anon.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}
