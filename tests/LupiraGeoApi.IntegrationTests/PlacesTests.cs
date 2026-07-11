using System.Net;
using System.Net.Http.Json;
using LupiraGeoApi.Domain;
using LupiraGeoApi.Dtos.Places;
using Xunit;

namespace LupiraGeoApi.IntegrationTests;

/// <summary>The gazetteer surface end-to-end over real PostGIS: create, get, proximity search, and free-text resolve.</summary>
public sealed class PlacesTests(GeoApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";

    private static async Task<PlaceDto> CreateAsync(HttpClient api, string name, double? lat = null, double? lon = null, PlaceCategory category = PlaceCategory.Unknown)
    {
        var resp = await api.PostAsJsonAsync("/places", new CreatePlaceRequest { Name = name, Latitude = lat, Longitude = lon, Category = category });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<PlaceDto>())!;
    }

    [Fact]
    public async Task Create_then_get_roundtrips_coordinates()
    {
        var api = Factory.ApiClient(Email);
        var created = await CreateAsync(api, "Cafe Central", 59.3293, 18.0686, PlaceCategory.Cafe);

        var got = (await api.GetFromJsonAsync<PlaceDto>($"/places/{created.Id}"))!;
        Assert.Equal("Cafe Central", got.Name);
        Assert.Equal(PlaceCategory.Cafe, got.Category);
        Assert.Equal(PlaceSource.User, got.Source);
        Assert.NotNull(got.Latitude);
        Assert.Equal(59.3293, got.Latitude!.Value, 4);
        Assert.Equal(18.0686, got.Longitude!.Value, 4);
    }

    [Fact]
    public async Task Near_search_returns_only_within_radius_ordered_by_distance()
    {
        var api = Factory.ApiClient(Email);
        await CreateAsync(api, "Near Cafe", 59.3293, 18.0686);   // Stockholm
        await CreateAsync(api, "Mid Cafe", 59.3400, 18.0700);    // ~1.2 km north
        await CreateAsync(api, "Far Cafe", 59.5000, 18.5000);    // tens of km away

        var hits = await api.GetFromJsonAsync<List<PlaceDto>>("/places?nearLat=59.3293&nearLon=18.0686&radiusM=5000");
        Assert.NotNull(hits);
        Assert.DoesNotContain(hits!, p => p.Name == "Far Cafe");
        Assert.Equal("Near Cafe", hits![0].Name);                // nearest first
        Assert.All(hits!, p => Assert.NotNull(p.DistanceM));
        Assert.True(hits![0].DistanceM < hits![1].DistanceM);
    }

    [Fact]
    public async Task Text_search_matches_case_insensitively()
    {
        var api = Factory.ApiClient(Email);
        await CreateAsync(api, "Riverside Gym");
        await CreateAsync(api, "Cafe Central");

        var hits = await api.GetFromJsonAsync<List<PlaceDto>>("/places?q=central");
        Assert.Contains(hits!, p => p.Name == "Cafe Central");
        Assert.DoesNotContain(hits!, p => p.Name == "Riverside Gym");
    }

    [Fact]
    public async Task Resolve_without_geocoder_provisions_an_unverified_user_place()
    {
        var api = Factory.ApiClient(Email);
        var resp = await api.PostAsJsonAsync("/places/resolve", new ResolvePlaceRequest { Text = "  Grandma's House  " });
        resp.EnsureSuccessStatusCode();
        var resolved = (await resp.Content.ReadFromJsonAsync<ResolvePlaceResponse>())!;
        Assert.Equal("Grandma's House", resolved.Name);          // trimmed/normalized
        Assert.Null(resolved.Latitude);

        // Resolving the same text again is idempotent — same id, no duplicate.
        var again = (await (await api.PostAsJsonAsync("/places/resolve", new ResolvePlaceRequest { Text = "grandma's house" }))
            .Content.ReadFromJsonAsync<ResolvePlaceResponse>())!;
        Assert.Equal(resolved.PlaceId, again.PlaceId);

        var got = (await api.GetFromJsonAsync<PlaceDto>($"/places/{resolved.PlaceId}"))!;
        Assert.Equal(PlaceSource.User, got.Source);
        Assert.False(got.Verified);
    }

    [Fact]
    public async Task Update_can_rename_and_verify()
    {
        var api = Factory.ApiClient(Email);
        var created = await CreateAsync(api, "Untitled");
        var resp = await api.PatchAsJsonAsync($"/places/{created.Id}", new UpdatePlaceRequest { Name = "Named", Verified = true });
        resp.EnsureSuccessStatusCode();
        var updated = (await resp.Content.ReadFromJsonAsync<PlaceDto>())!;
        Assert.Equal("Named", updated.Name);
        Assert.True(updated.Verified);
    }

    [Fact]
    public async Task Update_can_correct_coordinates_by_hand()
    {
        var api = Factory.ApiClient(Email);
        var created = await CreateAsync(api, "Wrong Spot", 59.0, 18.0);
        var resp = await api.PatchAsJsonAsync($"/places/{created.Id}", new UpdatePlaceRequest { Latitude = 60.5, Longitude = 15.2 });
        resp.EnsureSuccessStatusCode();
        var updated = (await resp.Content.ReadFromJsonAsync<PlaceDto>())!;
        Assert.Equal(60.5, updated.Latitude!.Value, 4);
        Assert.Equal(15.2, updated.Longitude!.Value, 4);
    }

    [Fact]
    public async Task Update_rejects_half_a_coordinate()
    {
        var api = Factory.ApiClient(Email);
        var created = await CreateAsync(api, "Half", 59.0, 18.0);
        var resp = await api.PatchAsJsonAsync($"/places/{created.Id}", new UpdatePlaceRequest { Latitude = 60.5 });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Delete_tombstones_place_and_hides_it_from_reads()
    {
        var api = Factory.ApiClient(Email);
        var created = await CreateAsync(api, "Bad Entry", 59.33, 18.06);

        var del = await api.DeleteAsync($"/places/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await api.GetAsync($"/places/{created.Id}")).StatusCode);
        var hits = await api.GetFromJsonAsync<List<PlaceDto>>("/places?q=Bad%20Entry");
        Assert.DoesNotContain(hits!, p => p.Id == created.Id);

        // Idempotent — deleting again is a no-op success, not a 404.
        Assert.Equal(HttpStatusCode.NoContent, (await api.DeleteAsync($"/places/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task Resolve_without_geocoder_reports_provisional()
    {
        var api = Factory.ApiClient(Email);
        var resolved = (await (await api.PostAsJsonAsync("/places/resolve", new ResolvePlaceRequest { Text = "Nowhere Special" }))
            .Content.ReadFromJsonAsync<ResolvePlaceResponse>())!;
        Assert.Equal(PlaceResolution.Provisional, resolved.Resolution);
        Assert.NotNull(resolved.PlaceId);

        var again = (await (await api.PostAsJsonAsync("/places/resolve", new ResolvePlaceRequest { Text = "Nowhere Special" }))
            .Content.ReadFromJsonAsync<ResolvePlaceResponse>())!;
        Assert.Equal(PlaceResolution.Matched, again.Resolution);   // second time matches the existing entry
    }

    [Fact]
    public async Task Get_unknown_place_is_404()
    {
        var api = Factory.ApiClient(Email);
        var resp = await api.GetAsync($"/places/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Unknown_external_id_is_404()
    {
        var api = Factory.ApiClient(Email);
        var resp = await api.GetAsync("/places/by-external/Osm/node/999999");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Batch_resolve_aligns_with_input_and_dedupes()
    {
        var api = Factory.ApiClient(Email);
        var resp = await api.PostAsJsonAsync("/places/resolve:batch",
            new ResolvePlacesBatchRequest { Texts = ["Cafe A", "Cafe B", "cafe a"] });
        resp.EnsureSuccessStatusCode();
        var resolved = (await resp.Content.ReadFromJsonAsync<List<ResolvePlaceResponse>>())!;

        Assert.Equal(3, resolved.Count);
        Assert.Equal("Cafe A", resolved[0].Name);
        Assert.Equal("Cafe B", resolved[1].Name);
        Assert.Equal(resolved[0].PlaceId, resolved[2].PlaceId); // same text → same place
        Assert.NotEqual(resolved[0].PlaceId, resolved[1].PlaceId);
    }

    [Fact]
    public async Task Batch_resolve_caps_the_batch_size()
    {
        var api = Factory.ApiClient(Email);
        var resp = await api.PostAsJsonAsync("/places/resolve:batch",
            new ResolvePlacesBatchRequest { Texts = [.. Enumerable.Range(0, 51).Select(i => $"Place {i}")] });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }
}
