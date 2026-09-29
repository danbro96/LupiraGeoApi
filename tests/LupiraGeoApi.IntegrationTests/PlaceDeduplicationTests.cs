using System.Net;
using System.Net.Http.Json;
using LupiraGeoApi.Core.Domain;
using LupiraGeoApi.Core.Dtos.Places;
using Xunit;

namespace LupiraGeoApi.IntegrationTests;

/// <summary>Name-fold dedup end-to-end: spelling variants of one address must land on one place, and the leftovers
/// from before that held must be findable. Nominatim is unset here, so every resolve takes the provisional path.</summary>
public sealed class PlaceDeduplicationTests(GeoApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";

    private static async Task<ResolvePlaceResponse> ResolveAsync(HttpClient api, string text)
    {
        var resp = await api.PostAsJsonAsync("/places/resolve", new ResolvePlaceRequest { Text = text });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<ResolvePlaceResponse>())!;
    }

    [Fact]
    public async Task Spelling_variants_of_one_address_resolve_to_one_place()
    {
        var api = Factory.ApiClient(Email);

        var first = await ResolveAsync(api, "Sveduddsvägen 31, 139 74 Djurhamn, Sweden");
        Assert.Equal(PlaceResolution.Provisional, first.Resolution);

        foreach (var variant in new[]
        {
            "Sveduddsvägen 31, SE-139 74 Djurhamn, Sverige",
            "sveduddsvägen 31, 139 74 djurhamn",
            "  Sveduddsvägen 31,   139 74 Djurhamn, SE ",
        })
        {
            var again = await ResolveAsync(api, variant);
            Assert.Equal(PlaceResolution.Matched, again.Resolution);
            Assert.Equal(first.PlaceId, again.PlaceId);
        }

        var unlocated = (await api.GetFromJsonAsync<List<PlaceDto>>("/places?hasCoordinates=false"))!;
        Assert.Single(unlocated);
    }

    [Fact]
    public async Task A_different_postcode_is_a_different_place()
    {
        var api = Factory.ApiClient(Email);
        var a = await ResolveAsync(api, "Lerbo Lilla Navesta 1, 640 23 Valla");
        var b = await ResolveAsync(api, "Lerbo Lilla Navesta 1, 641 62 Valla");
        Assert.NotEqual(a.PlaceId, b.PlaceId);
    }

    [Fact]
    public async Task Creating_a_place_that_folds_onto_an_existing_one_is_rejected()
    {
        var api = Factory.ApiClient(Email);
        (await api.PostAsJsonAsync("/places", new CreatePlaceRequest { Name = "Bio Skandia, Drottninggatan 82" }))
            .EnsureSuccessStatusCode();

        var clash = await api.PostAsJsonAsync("/places",
            new CreatePlaceRequest { Name = "bio skandia, drottninggatan 82, Sweden" });
        Assert.Equal(HttpStatusCode.Conflict, clash.StatusCode);
    }

    [Fact]
    public async Task Merging_makes_the_losers_spelling_resolve_to_the_survivor()
    {
        var api = Factory.ApiClient(Email);
        var survivor = await ResolveAsync(api, "Bergshamra station, Solna");
        var loser = await ResolveAsync(api, "Bergshamra T-Bana, Kraus väg 2, 170 77 Solna");
        Assert.NotEqual(survivor.PlaceId, loser.PlaceId);

        (await api.PostAsJsonAsync($"/places/{loser.PlaceId}/merge",
            new MergePlaceRequest { IntoPlaceId = survivor.PlaceId!.Value })).EnsureSuccessStatusCode();

        var again = await ResolveAsync(api, "Bergshamra T-Bana, Kraus väg 2, 170 77 Solna, Sweden");
        Assert.Equal(PlaceResolution.Matched, again.Resolution);
        Assert.Equal(survivor.PlaceId, again.PlaceId);
    }

    [Fact]
    public async Task Co_located_places_with_similar_names_surface_as_duplicate_candidates()
    {
        var api = Factory.ApiClient(Email);
        foreach (var name in new[] { "Cafe Central", "Cafe Central Bar" })
            (await api.PostAsJsonAsync("/places",
                new CreatePlaceRequest { Name = name, Latitude = 59.3293, Longitude = 18.0686 })).EnsureSuccessStatusCode();

        // A third place far away must not join the cluster.
        (await api.PostAsJsonAsync("/places",
            new CreatePlaceRequest { Name = "Cafe Central Malmö", Latitude = 55.6050, Longitude = 13.0038 })).EnsureSuccessStatusCode();

        var clusters = (await api.GetFromJsonAsync<List<DuplicateClusterDto>>("/places/duplicates"))!;
        var cluster = Assert.Single(clusters);
        Assert.Equal(DuplicateReason.CoLocated, cluster.Reason);
        Assert.Equal(2, cluster.Places.Count);
        Assert.Equal(0, cluster.SpreadM!.Value, 1);
    }

    [Fact]
    public async Task Batch_regeocode_reports_per_item_and_does_not_abort()
    {
        var api = Factory.ApiClient(Email);
        var stub = await ResolveAsync(api, "Somewhere With No Fix");
        var missing = Guid.NewGuid();

        var resp = await api.PostAsJsonAsync("/places/regeocode:batch",
            new RegeocodePlacesBatchRequest { PlaceIds = [stub.PlaceId!.Value, missing] });
        resp.EnsureSuccessStatusCode();
        var results = (await resp.Content.ReadFromJsonAsync<List<RegeocodePlaceResultDto>>())!;

        Assert.Equal(2, results.Count);
        // Nominatim is unset: a definitive empty answer, not an outage.
        Assert.Equal(RegeocodeStatus.NoHit, results[0].Status);
        Assert.Equal(RegeocodeStatus.NotFound, results[1].Status);
    }
}
