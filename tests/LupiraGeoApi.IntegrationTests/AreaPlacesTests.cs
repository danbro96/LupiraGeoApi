using System.Net.Http.Json;
using Lupira.Testing.Postgres;
using LupiraGeoApi.Core.Domain;
using LupiraGeoApi.Core.Dtos.Curation;
using LupiraGeoApi.Core.Dtos.Places;
using Xunit;

namespace LupiraGeoApi.IntegrationTests;

/// <summary>City-level geocodes become <see cref="PlaceKind.Area"/>: on resolve, by hand via PATCH, and — for places made
/// before classification existed — by the curation sweep over the frozen geocode cache.</summary>
[Collection("geocoding")]
public sealed class AreaPlacesTests(GeocodingFixture fx) : IAsyncLifetime
{
    const string Email = "alice@x.test";
    const string CityOsmId = "relation/13048688";

    public Task InitializeAsync()
    {
        fx.Fallback.CityHit = true;
        return fx.Factory.ResetAsync();
    }

    public Task DisposeAsync()
    {
        fx.Fallback.CityHit = false;
        return Task.CompletedTask;
    }

    private static async Task<PlaceDto> GetAsync(HttpClient api, Guid id) =>
        (await api.GetFromJsonAsync<PlaceDto>($"/places/{id}"))!;

    private static async Task<Guid> CreatePoiAsync(HttpClient api, string name)
    {
        var resp = await api.PostAsJsonAsync("/places", new CreatePlaceRequest { Name = name, Latitude = 56.95, Longitude = 24.11 });
        return (await resp.EnsureSuccessStatusCode().Content.ReadFromJsonAsync<PlaceDto>())!.Id;
    }

    private static async Task<List<AreaReclassificationDto>> ClassifyAsync(HttpClient api, bool apply)
    {
        var resp = await api.PostAsync($"/curation/classify-areas?apply={apply.ToString().ToLowerInvariant()}", null);
        return (await resp.EnsureSuccessStatusCode().Content.ReadFromJsonAsync<List<AreaReclassificationDto>>())!;
    }

    [Fact]
    public async Task Resolving_free_text_that_matches_a_city_creates_an_area()
    {
        var api = fx.Factory.ApiClient(Email);

        var resp = await api.PostAsJsonAsync("/places/resolve", new ResolvePlaceRequest { Text = "Riga, Latvia" });
        var resolved = (await resp.EnsureSuccessStatusCode().Content.ReadFromJsonAsync<ResolvePlaceResponse>())!;

        Assert.Equal(PlaceResolution.Geocoded, resolved.Resolution);
        Assert.Equal(PlaceKind.Area, (await GetAsync(api, resolved.PlaceId!.Value)).Kind);
    }

    [Fact]
    public async Task The_sweep_reclassifies_an_uncategorized_place_whose_cached_geocode_is_an_area()
    {
        var api = fx.Factory.ApiClient(Email);
        (await api.GetAsync("/geocode/forward?q=Riga")).EnsureSuccessStatusCode();
        var legacy = await CreatePoiAsync(api, "Riga, Latvia");
        (await api.PostAsJsonAsync($"/places/{legacy}/external-ids",
            new AddExternalIdRequest { Scheme = ExternalScheme.Osm, Value = CityOsmId })).EnsureSuccessStatusCode();
        await CreatePoiAsync(api, "Riga RIX office");

        var preview = Assert.Single(await ClassifyAsync(api, apply: false));
        Assert.Equal((legacy, CityOsmId, false), (preview.PlaceId, preview.OsmId, preview.Applied));
        Assert.Equal(PlaceKind.Poi, (await GetAsync(api, legacy)).Kind);

        Assert.True(Assert.Single(await ClassifyAsync(api, apply: true)).Applied);
        Assert.Equal(PlaceKind.Area, (await GetAsync(api, legacy)).Kind);
        var history = (await api.GetFromJsonAsync<List<CurationEventDto>>($"/places/{legacy}/history"))!;
        Assert.Contains(history, e => e.Action == CurationAction.Reclassified);
        Assert.Empty(await ClassifyAsync(api, apply: false));
    }

    [Fact]
    public async Task A_category_marks_a_place_as_a_curated_venue_the_sweep_leaves_alone()
    {
        var api = fx.Factory.ApiClient(Email);
        (await api.GetAsync("/geocode/forward?q=Riga")).EnsureSuccessStatusCode();
        var office = await CreatePoiAsync(api, "Riga, Latvia");
        (await api.PostAsJsonAsync($"/places/{office}/external-ids",
            new AddExternalIdRequest { Scheme = ExternalScheme.Osm, Value = CityOsmId })).EnsureSuccessStatusCode();
        (await api.PatchAsJsonAsync($"/places/{office}", new UpdatePlaceRequest { Category = PlaceCategory.Office })).EnsureSuccessStatusCode();

        Assert.Empty(await ClassifyAsync(api, apply: true));
        Assert.Equal(PlaceKind.Poi, (await GetAsync(api, office)).Kind);
    }

    [Fact]
    public async Task Kind_is_curatable_by_hand()
    {
        var api = fx.Factory.ApiClient(Email);
        var id = await CreatePoiAsync(api, "Stockholm, Sweden");

        var resp = await api.PatchAsJsonAsync($"/places/{id}", new UpdatePlaceRequest { Kind = PlaceKind.Area });

        Assert.Equal(PlaceKind.Area, (await resp.EnsureSuccessStatusCode().Content.ReadFromJsonAsync<PlaceDto>())!.Kind);
    }
}
