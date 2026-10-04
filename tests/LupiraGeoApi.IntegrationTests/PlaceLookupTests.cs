using System.Net;
using System.Net.Http.Json;
using Lupira.Testing.Postgres;
using LupiraGeoApi.Core.Dtos.Places;
using Xunit;

namespace LupiraGeoApi.IntegrationTests;

/// <summary>Bulk get-by-ids: index alignment, merge-redirect following, null on missing/deleted, and the batch cap.</summary>
public sealed class PlaceLookupTests(GeoApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";

    private static async Task<PlaceDto> CreateAsync(HttpClient api, string name)
    {
        var resp = await api.PostAsJsonAsync("/places", new CreatePlaceRequest { Name = name, Latitude = 59.33, Longitude = 18.07 });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<PlaceDto>())!;
    }

    private static async Task<List<PlaceLookupItemDto>> LookupAsync(HttpClient api, params Guid[] ids)
    {
        var resp = await api.PostAsJsonAsync("/places/lookup", new LookupPlacesRequest { Ids = [.. ids] });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<List<PlaceLookupItemDto>>())!;
    }

    [Fact]
    public async Task Lookup_aligns_with_input_and_nulls_unknown_ids()
    {
        var api = Factory.ApiClient(Email);
        var a = await CreateAsync(api, "Alpha");
        var b = await CreateAsync(api, "Beta");
        var unknown = Guid.NewGuid();

        var items = await LookupAsync(api, a.Id, unknown, b.Id, a.Id);

        Assert.Equal(4, items.Count);
        Assert.Equal(a.Id, items[0].RequestedId);
        Assert.Equal("Alpha", items[0].Place!.Name);
        Assert.Equal(unknown, items[1].RequestedId);
        Assert.Null(items[1].Place);
        Assert.Equal("Beta", items[2].Place!.Name);
        Assert.Equal("Alpha", items[3].Place!.Name); // duplicates allowed
        Assert.All(items.Where(i => i.Place is not null), i => Assert.Empty(i.Place!.Containment));
    }

    [Fact]
    public async Task Merged_id_returns_the_survivor()
    {
        var api = Factory.ApiClient(Email);
        var duplicate = await CreateAsync(api, "Dup");
        var survivor = await CreateAsync(api, "Survivor");

        var merge = await api.PostAsJsonAsync($"/places/{duplicate.Id}/merge", new MergePlaceRequest { IntoPlaceId = survivor.Id });
        merge.EnsureSuccessStatusCode();

        var items = await LookupAsync(api, duplicate.Id);
        Assert.Equal(duplicate.Id, items[0].RequestedId);
        Assert.Equal(survivor.Id, items[0].Place!.Id);
    }

    [Fact]
    public async Task Deleted_id_returns_null()
    {
        var api = Factory.ApiClient(Email);
        var place = await CreateAsync(api, "Doomed");
        (await api.DeleteAsync($"/places/{place.Id}")).EnsureSuccessStatusCode();

        var items = await LookupAsync(api, place.Id);
        Assert.Null(items[0].Place);
    }

    [Fact]
    public async Task Empty_and_oversized_batches_are_rejected()
    {
        var api = Factory.ApiClient(Email);

        var empty = await api.PostAsJsonAsync("/places/lookup", new LookupPlacesRequest { Ids = [] });
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);

        var oversized = await api.PostAsJsonAsync("/places/lookup",
            new LookupPlacesRequest { Ids = [.. Enumerable.Range(0, 201).Select(_ => Guid.NewGuid())] });
        Assert.Equal(HttpStatusCode.BadRequest, oversized.StatusCode);
    }

    [Fact]
    public async Task Anonymous_lookup_is_unauthorized()
    {
        var resp = await Factory.AnonymousClient()
            .PostAsJsonAsync("/places/lookup", new LookupPlacesRequest { Ids = [Guid.NewGuid()] });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}
