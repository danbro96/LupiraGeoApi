using LupiraGeoApi.Data;
using LupiraGeoApi.Domain;
using LupiraGeoApi.Dtos.Places;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using System.Net.Http.Json;
using System.Net;
using Xunit;

namespace LupiraGeoApi.IntegrationTests;

/// <summary>Trigram typeahead over places (names + aliases) and seeded AdminArea localities.</summary>
public sealed class PlaceSuggestTests(GeoApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";

    private static async Task<PlaceDto> CreateAsync(HttpClient api, string name)
    {
        var resp = await api.PostAsJsonAsync("/places", new CreatePlaceRequest { Name = name });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<PlaceDto>())!;
    }

    private async Task SeedLocalityAsync(string name, string regionName, double lat, double lon)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GeoDbContext>();
        var region = new AdminArea { Id = Guid.NewGuid(), Level = AdminLevel.Region, Name = regionName };
        db.AdminAreas.Add(region);
        db.AdminAreas.Add(new AdminArea
        {
            Id = Guid.NewGuid(), Level = AdminLevel.Locality, Name = name,
            WithinAreaId = region.Id, Centroid = new Point(lon, lat) { SRID = 4326 },
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Typo_tolerant_place_match()
    {
        var api = Factory.ApiClient(Email);
        await CreateAsync(api, "Cafe Central");
        await CreateAsync(api, "Riverside Gym");

        var hits = (await api.GetFromJsonAsync<List<PlaceSuggestionDto>>("/places/suggest?q=centrall"))!;
        Assert.Contains(hits, s => s.Name == "Cafe Central" && s.Type == SuggestionType.Place);
        Assert.DoesNotContain(hits, s => s.Name == "Riverside Gym");
    }

    [Fact]
    public async Task Locality_from_the_gazetteer_seed_suggests_with_context()
    {
        var api = Factory.ApiClient(Email);
        await SeedLocalityAsync("Stockholm", "Stockholms län", 59.3293, 18.0686);

        var hits = (await api.GetFromJsonAsync<List<PlaceSuggestionDto>>("/places/suggest?q=stokholm"))!;
        var locality = Assert.Single(hits, s => s.Type == SuggestionType.Locality);
        Assert.Equal("Stockholm", locality.Name);
        Assert.Equal("Stockholms län", locality.Context);
        Assert.NotNull(locality.Latitude);
    }

    [Fact]
    public async Task Alias_prefix_matches()
    {
        var api = Factory.ApiClient(Email);
        var place = await CreateAsync(api, "Karolinska Universitetssjukhuset");
        (await api.PostAsJsonAsync($"/places/{place.Id}/aliases", new AddAliasRequest { Name = "KS" })).EnsureSuccessStatusCode();

        var hits = (await api.GetFromJsonAsync<List<PlaceSuggestionDto>>("/places/suggest?q=KS"))!;
        Assert.Contains(hits, s => s.Id == place.Id);
    }

    [Fact]
    public async Task Tombstones_do_not_suggest()
    {
        var api = Factory.ApiClient(Email);
        var winner = await CreateAsync(api, "Cafe Central");
        var loser = await CreateAsync(api, "Cafe Centralen");
        (await api.PostAsJsonAsync($"/places/{loser.Id}/merge", new MergePlaceRequest { IntoPlaceId = winner.Id })).EnsureSuccessStatusCode();

        var hits = (await api.GetFromJsonAsync<List<PlaceSuggestionDto>>("/places/suggest?q=centralen"))!;
        Assert.DoesNotContain(hits, s => s.Id == loser.Id);
        Assert.Contains(hits, s => s.Id == winner.Id); // loser's name lives on as the winner's alias
    }

    [Fact]
    public async Task Blank_query_is_rejected()
    {
        var api = Factory.ApiClient(Email);
        var resp = await api.GetAsync("/places/suggest?q=");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }
}
