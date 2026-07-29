using LupiraGeoApi.Dtos.Places;
using System.Net.Http.Json;
using System.Net;
using Xunit;

namespace LupiraGeoApi.IntegrationTests;

/// <summary>Alias management + the resolver matching aliases as stage-1 hits.</summary>
public sealed class PlaceAliasesTests(GeoApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";

    private static async Task<PlaceDto> CreateAsync(HttpClient api, string name)
    {
        var resp = await api.PostAsJsonAsync("/places", new CreatePlaceRequest { Name = name });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<PlaceDto>())!;
    }

    [Fact]
    public async Task Add_alias_then_get_shows_it()
    {
        var api = Factory.ApiClient(Email);
        var place = await CreateAsync(api, "Stockholms centralstation");

        var resp = await api.PostAsJsonAsync($"/places/{place.Id}/aliases", new AddAliasRequest { Name = "Centralen", Lang = "sv" });
        resp.EnsureSuccessStatusCode();
        var updated = (await resp.Content.ReadFromJsonAsync<PlaceDto>())!;
        Assert.Contains(updated.Aliases, a => a.Name == "Centralen" && a.Lang == "sv");

        var got = (await api.GetFromJsonAsync<PlaceDto>($"/places/{place.Id}"))!;
        Assert.Contains(got.Aliases, a => a.Name == "Centralen");
    }

    [Fact]
    public async Task Duplicate_or_canonical_alias_is_conflict()
    {
        var api = Factory.ApiClient(Email);
        var place = await CreateAsync(api, "Cafe Central");
        (await api.PostAsJsonAsync($"/places/{place.Id}/aliases", new AddAliasRequest { Name = "CC" })).EnsureSuccessStatusCode();

        var dup = await api.PostAsJsonAsync($"/places/{place.Id}/aliases", new AddAliasRequest { Name = "cc" });
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);

        var canonical = await api.PostAsJsonAsync($"/places/{place.Id}/aliases", new AddAliasRequest { Name = "cafe central" });
        Assert.Equal(HttpStatusCode.Conflict, canonical.StatusCode);
    }

    [Fact]
    public async Task Delete_alias_removes_it_and_is_404_after()
    {
        var api = Factory.ApiClient(Email);
        var place = await CreateAsync(api, "Cafe Central");
        var withAlias = (await (await api.PostAsJsonAsync($"/places/{place.Id}/aliases", new AddAliasRequest { Name = "CC" }))
            .Content.ReadFromJsonAsync<PlaceDto>())!;
        var aliasId = withAlias.Aliases.Single(a => a.Name == "CC").Id;

        var del = await api.DeleteAsync($"/places/{place.Id}/aliases/{aliasId}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var got = (await api.GetFromJsonAsync<PlaceDto>($"/places/{place.Id}"))!;
        Assert.DoesNotContain(got.Aliases, a => a.Name == "CC");

        var again = await api.DeleteAsync($"/places/{place.Id}/aliases/{aliasId}");
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
    }

    [Fact]
    public async Task Resolve_matches_an_alias_instead_of_creating_a_duplicate()
    {
        var api = Factory.ApiClient(Email);
        var place = await CreateAsync(api, "Stockholms centralstation");
        (await api.PostAsJsonAsync($"/places/{place.Id}/aliases", new AddAliasRequest { Name = "Centralen" })).EnsureSuccessStatusCode();

        var resp = await api.PostAsJsonAsync("/places/resolve", new ResolvePlaceRequest { Text = "centralen" });
        resp.EnsureSuccessStatusCode();
        var resolved = (await resp.Content.ReadFromJsonAsync<ResolvePlaceResponse>())!;
        Assert.Equal(place.Id, resolved.PlaceId);
    }
}
