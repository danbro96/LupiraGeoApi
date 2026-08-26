using System.Net;
using System.Net.Http.Headers;
using LupiraGeoApi.Endpoints;
using Xunit;

namespace LupiraGeoApi.IntegrationTests;

[Collection("basemap")]
public sealed class BasemapTests(BasemapFixture fx)
{
    const string Email = "alice@x.test";

    [Theory]
    [InlineData(null, "/sprites/v4/light")]
    [InlineData("dark", "/sprites/v4/dark")]
    public async Task Style_replaces_base_per_theme(string? theme, string expectedSprite)
    {
        var api = fx.Factory.ApiClient(Email);
        var resp = await api.GetAsync($"/basemap/style.json{(theme is null ? "" : $"?theme={theme}")}");
        resp.EnsureSuccessStatusCode();
        Assert.Equal("application/json", resp.Content.Headers.ContentType!.MediaType);

        var json = await resp.Content.ReadAsStringAsync();
        Assert.DoesNotContain("{BASE}", json);
        Assert.Contains("pmtiles:///geo-api/basemap/tiles/basemap.pmtiles", json);
        Assert.Contains($"/geo-api/basemap{expectedSprite}", json);
        Assert.Contains("/geo-api/basemap/fonts/{fontstack}/{range}.pbf", json);
    }

    [Fact]
    public async Task Unknown_theme_is_rejected()
    {
        var resp = await fx.Factory.ApiClient(Email).GetAsync("/basemap/style.json?theme=sepia");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Pmtiles_serves_whole_and_range()
    {
        var api = fx.Factory.ApiClient(Email);

        var whole = await api.GetAsync("/basemap/tiles/basemap.pmtiles");
        whole.EnsureSuccessStatusCode();
        Assert.Equal("application/octet-stream", whole.Content.Headers.ContentType!.MediaType);
        Assert.Equal(64, (await whole.Content.ReadAsByteArrayAsync()).Length);

        var req = new HttpRequestMessage(HttpMethod.Get, "/basemap/tiles/basemap.pmtiles");
        req.Headers.Range = new RangeHeaderValue(0, 3);
        var partial = await api.SendAsync(req);
        Assert.Equal(HttpStatusCode.PartialContent, partial.StatusCode);
        Assert.Equal("bytes 0-3/64", partial.Content.Headers.ContentRange!.ToString());
        Assert.Equal(new byte[] { 0, 1, 2, 3 }, await partial.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Unknown_asset_is_not_found()
    {
        var resp = await fx.Factory.ApiClient(Email).GetAsync("/basemap/tiles/nope.pmtiles");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Anonymous_requests_are_unauthorized()
    {
        var anon = fx.Factory.AnonymousClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/basemap/style.json")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/basemap/tiles/basemap.pmtiles")).StatusCode);
    }

    [Fact]
    public async Task Style_serves_from_bundled_template_when_assets_unset()
    {
        // A factory without Basemap:AssetsPath: style.json still works (committed template), assets 404.
        await using var bare = new GeoApiTestFactory();
        var api = bare.ApiClient(Email);
        (await api.GetAsync("/basemap/style.json")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await api.GetAsync("/basemap/tiles/basemap.pmtiles")).StatusCode);
    }

    [Fact]
    public void Traversal_and_rooted_paths_resolve_to_null()
    {
        Assert.Null(BasemapEndpoints.TryResolveAsset(fx.AssetsDir, "../outside.txt"));
        Assert.Null(BasemapEndpoints.TryResolveAsset(fx.AssetsDir, "tiles/../../outside.txt"));
        Assert.Null(BasemapEndpoints.TryResolveAsset(fx.AssetsDir, "/etc/hostname"));
        Assert.Null(BasemapEndpoints.TryResolveAsset(null, "tiles/basemap.pmtiles"));
        Assert.NotNull(BasemapEndpoints.TryResolveAsset(fx.AssetsDir, "tiles/basemap.pmtiles"));
    }
}
