using System.Net;
using System.Net.Http.Headers;
using LupiraGeoApi.Endpoints;
using Xunit;

namespace LupiraGeoApi.IntegrationTests;

/// <summary>Own factory: the shared one leaves Basemap:AssetsPath unset, this one points it at a seeded temp dir.</summary>
public sealed class BasemapFixture : IAsyncLifetime
{
    public string AssetsDir { get; private set; } = null!;
    public GeoApiTestFactory Factory { get; private set; } = null!;

    public Task InitializeAsync()
    {
        AssetsDir = Directory.CreateTempSubdirectory("geo-basemap-tests").FullName;
        Directory.CreateDirectory(Path.Combine(AssetsDir, "tiles"));
        File.WriteAllBytes(Path.Combine(AssetsDir, "tiles", "basemap.pmtiles"),
            [.. Enumerable.Range(0, 64).Select(i => (byte) i)]);
        Factory = new GeoApiTestFactory();
        Factory.ExtraConfig["Basemap:AssetsPath"] = AssetsDir;
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        Directory.Delete(AssetsDir, recursive: true);
    }
}
