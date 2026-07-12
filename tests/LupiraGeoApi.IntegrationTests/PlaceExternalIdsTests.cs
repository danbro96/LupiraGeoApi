using System.Net;
using System.Net.Http.Json;
using LupiraGeoApi.Domain;
using LupiraGeoApi.Dtos.Places;
using Xunit;

namespace LupiraGeoApi.IntegrationTests;

/// <summary>External-id curation: add/remove, multiple per scheme, and the global (Scheme,Value) uniqueness.</summary>
public sealed class PlaceExternalIdsTests(GeoApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";

    private static async Task<PlaceDto> CreateAsync(HttpClient api, string name)
    {
        var resp = await api.PostAsJsonAsync("/places", new CreatePlaceRequest { Name = name });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<PlaceDto>())!;
    }

    private static Task<HttpResponseMessage> AddAsync(HttpClient api, Guid id, ExternalScheme scheme, string value) =>
        api.PostAsJsonAsync($"/places/{id}/external-ids", new AddExternalIdRequest { Scheme = scheme, Value = value });

    [Fact]
    public async Task Add_then_get_and_by_external_show_it()
    {
        var api = Factory.ApiClient(Email);
        var place = await CreateAsync(api, "Kebnekaise");

        var resp = await AddAsync(api, place.Id, ExternalScheme.Osm, "node/1");
        resp.EnsureSuccessStatusCode();
        var updated = (await resp.Content.ReadFromJsonAsync<PlaceDto>())!;
        Assert.Contains(updated.ExternalIds, x => x.Scheme == ExternalScheme.Osm && x.Value == "node/1");

        var got = (await api.GetFromJsonAsync<PlaceDto>($"/places/{place.Id}"))!;
        Assert.Contains(got.ExternalIds, x => x.Value == "node/1");

        var byExternal = (await api.GetFromJsonAsync<PlaceDto>("/places/by-external/Osm/node/1"))!;
        Assert.Equal(place.Id, byExternal.Id);
    }

    [Fact]
    public async Task Multiple_ids_per_scheme_are_allowed()
    {
        var api = Factory.ApiClient(Email);
        var place = await CreateAsync(api, "Home");
        (await AddAsync(api, place.Id, ExternalScheme.Osm, "way/1")).EnsureSuccessStatusCode();
        (await AddAsync(api, place.Id, ExternalScheme.Osm, "node/2")).EnsureSuccessStatusCode();

        var got = (await api.GetFromJsonAsync<PlaceDto>($"/places/{place.Id}"))!;
        Assert.Contains(got.ExternalIds, x => x.Value == "way/1");
        Assert.Contains(got.ExternalIds, x => x.Value == "node/2");
    }

    [Fact]
    public async Task Cross_place_same_scheme_value_is_conflict()
    {
        var api = Factory.ApiClient(Email);
        var a = await CreateAsync(api, "Place A");
        var b = await CreateAsync(api, "Place B");
        (await AddAsync(api, a.Id, ExternalScheme.Osm, "node/900")).EnsureSuccessStatusCode();

        var conflict = await AddAsync(api, b.Id, ExternalScheme.Osm, "node/900");
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
    }

    [Fact]
    public async Task Duplicate_on_same_place_is_conflict()
    {
        var api = Factory.ApiClient(Email);
        var place = await CreateAsync(api, "Place");
        (await AddAsync(api, place.Id, ExternalScheme.Osm, "way/5")).EnsureSuccessStatusCode();

        var dup = await AddAsync(api, place.Id, ExternalScheme.Osm, "way/5");
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
    }

    [Fact]
    public async Task Remove_detaches_it_and_is_404_after()
    {
        var api = Factory.ApiClient(Email);
        var place = await CreateAsync(api, "Place");
        (await AddAsync(api, place.Id, ExternalScheme.Osm, "node/1")).EnsureSuccessStatusCode();

        var del = await api.DeleteAsync($"/places/{place.Id}/external-ids/Osm/node/1");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var got = (await api.GetFromJsonAsync<PlaceDto>($"/places/{place.Id}"))!;
        Assert.DoesNotContain(got.ExternalIds, x => x.Value == "node/1");

        var again = await api.DeleteAsync($"/places/{place.Id}/external-ids/Osm/node/1");
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
    }

    [Fact]
    public async Task Remove_unknown_is_404()
    {
        var api = Factory.ApiClient(Email);
        var place = await CreateAsync(api, "Place");
        var resp = await api.DeleteAsync($"/places/{place.Id}/external-ids/Osm/way/999");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Add_to_unknown_place_is_404()
    {
        var api = Factory.ApiClient(Email);
        var resp = await AddAsync(api, Guid.NewGuid(), ExternalScheme.Osm, "node/1");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
