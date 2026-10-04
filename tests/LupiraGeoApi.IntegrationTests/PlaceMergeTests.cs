using System.Net;
using System.Net.Http.Json;
using Lupira.Testing.Postgres;
using LupiraGeoApi.Core.Domain;
using LupiraGeoApi.Core.Dtos.Places;
using LupiraGeoApi.Core.Dtos.SavedPlaces;
using Xunit;

namespace LupiraGeoApi.IntegrationTests;

/// <summary>Merge with tombstone redirect: names move over as aliases, saved places re-point, the loser id keeps
/// resolving, and tombstones vanish from search.</summary>
public sealed class PlaceMergeTests(GeoApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";

    private static async Task<PlaceDto> CreateAsync(
        HttpClient api, string name, double? lat = null, double? lon = null, PlaceCategory category = PlaceCategory.Unknown, string? address = null)
    {
        var resp = await api.PostAsJsonAsync("/places", new CreatePlaceRequest
        {
            Name = name, Latitude = lat, Longitude = lon, Category = category, FormattedAddress = address,
        });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<PlaceDto>())!;
    }

    private static async Task<PlaceDto> MergeAsync(HttpClient api, Guid id, Guid intoId)
    {
        var resp = await api.PostAsJsonAsync($"/places/{id}/merge", new MergePlaceRequest { IntoPlaceId = intoId });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<PlaceDto>())!;
    }

    [Fact]
    public async Task Merge_moves_names_repoints_saved_places_and_redirects()
    {
        var api = Factory.ApiClient(Email);
        var winner = await CreateAsync(api, "Cafe Central", 59.3293, 18.0686, PlaceCategory.Cafe);
        var loser = await CreateAsync(api, "Café Central Stockholm");
        (await api.PostAsJsonAsync($"/places/{loser.Id}/aliases", new AddAliasRequest { Name = "CC" })).EnsureSuccessStatusCode();
        (await api.PostAsJsonAsync("/me/places", new CreateSavedPlaceRequest { Label = "Fave cafe", PlaceId = loser.Id })).EnsureSuccessStatusCode();

        var merged = await MergeAsync(api, loser.Id, winner.Id);
        Assert.Equal(winner.Id, merged.Id);
        Assert.Contains(merged.Aliases, a => a.Name == "Café Central Stockholm");
        Assert.Contains(merged.Aliases, a => a.Name == "CC");

        // The loser id keeps resolving — transparently redirected to the survivor.
        var redirected = (await api.GetFromJsonAsync<PlaceDto>($"/places/{loser.Id}"))!;
        Assert.Equal(winner.Id, redirected.Id);

        // Tombstones are excluded from search.
        var hits = (await api.GetFromJsonAsync<List<PlaceDto>>("/places?q=Central"))!;
        Assert.DoesNotContain(hits, p => p.Id == loser.Id);
        Assert.Contains(hits, p => p.Id == winner.Id);

        // The saved place re-pointed.
        var saved = (await api.GetFromJsonAsync<List<SavedPlaceDto>>("/me/places"))!;
        Assert.Equal(winner.Id, saved.Single().PlaceId);
    }

    private static async Task AddExternalIdAsync(HttpClient api, Guid id, ExternalScheme scheme, string value) =>
        (await api.PostAsJsonAsync($"/places/{id}/external-ids", new AddExternalIdRequest { Scheme = scheme, Value = value }))
            .EnsureSuccessStatusCode();

    [Fact]
    public async Task Merge_fills_missing_fields_from_the_loser()
    {
        var api = Factory.ApiClient(Email);
        var winner = await CreateAsync(api, "Cafe Central");
        var loser = await CreateAsync(api, "Cafe Central Annex", 59.3293, 18.0686, PlaceCategory.Cafe, "Vasagatan 1, Stockholm");
        await AddExternalIdAsync(api, loser.Id, ExternalScheme.Osm, "node/11");

        var merged = await MergeAsync(api, loser.Id, winner.Id);
        Assert.Equal(59.3293, merged.Latitude);
        Assert.Equal("Vasagatan 1, Stockholm", merged.FormattedAddress);
        Assert.Contains(merged.ExternalIds, x => x is { Scheme: ExternalScheme.Osm, Value: "node/11" });
        Assert.Equal(PlaceCategory.Cafe, merged.Category);
    }

    [Fact]
    public async Task Located_survivor_keeps_its_own_fix_and_drops_the_losers_osm_id()
    {
        var api = Factory.ApiClient(Email);
        var winner = await CreateAsync(api, "Mulle Meck i Glada Hudik", 61.7278494, 17.1084592, PlaceCategory.Landmark);
        var loser = await CreateAsync(api, "Mulle Meck", 59.33, 18.21, PlaceCategory.Store, "Käppuddsgatan 1, Lidingö");
        await AddExternalIdAsync(api, loser.Id, ExternalScheme.Osm, "node/1375840275");
        await AddExternalIdAsync(api, loser.Id, ExternalScheme.Wikidata, "Q1");

        var merged = await MergeAsync(api, loser.Id, winner.Id);

        Assert.Equal(61.7278494, merged.Latitude);
        Assert.Equal(17.1084592, merged.Longitude);
        Assert.Null(merged.FormattedAddress);
        Assert.Equal("Mulle Meck i Glada Hudik", merged.Name);
        Assert.Equal(PlaceCategory.Landmark, merged.Category);
        Assert.DoesNotContain(merged.ExternalIds, x => x.Scheme == ExternalScheme.Osm);
        Assert.Contains(merged.ExternalIds, x => x is { Scheme: ExternalScheme.Wikidata, Value: "Q1" });
        Assert.Contains(merged.Aliases, a => a.Name == "Mulle Meck");

        var byOsm = await api.GetAsync("/places/by-external/Osm/node/1375840275");
        Assert.Equal(HttpStatusCode.NotFound, byOsm.StatusCode);

        var history = (await api.GetFromJsonAsync<List<CurationEventDto>>($"/places/{loser.Id}/history"))!;
        Assert.Contains(history, e => e.Action == CurationAction.ExternalIdRemoved && e.Detail == "Osm:node/1375840275");
    }

    [Fact]
    public async Task Survivor_keeps_its_external_id_where_the_loser_has_one_of_the_same_scheme()
    {
        var api = Factory.ApiClient(Email);
        var winner = await CreateAsync(api, "Kulturhuset");
        var loser = await CreateAsync(api, "Kulturhuset Stadsteatern");
        await AddExternalIdAsync(api, winner.Id, ExternalScheme.Wikidata, "Q2");
        await AddExternalIdAsync(api, loser.Id, ExternalScheme.Wikidata, "Q3");
        await AddExternalIdAsync(api, loser.Id, ExternalScheme.Osm, "way/7");

        var merged = await MergeAsync(api, loser.Id, winner.Id);

        Assert.Equal("Q2", Assert.Single(merged.ExternalIds, x => x.Scheme == ExternalScheme.Wikidata).Value);
        Assert.Contains(merged.ExternalIds, x => x is { Scheme: ExternalScheme.Osm, Value: "way/7" });
    }

    [Fact]
    public async Task Merge_is_idempotent_but_conflicting_targets_are_rejected()
    {
        var api = Factory.ApiClient(Email);
        var a = await CreateAsync(api, "Place A");
        var b = await CreateAsync(api, "Place B");
        var c = await CreateAsync(api, "Place C");

        Assert.Equal(b.Id, (await MergeAsync(api, a.Id, b.Id)).Id);
        Assert.Equal(b.Id, (await MergeAsync(api, a.Id, b.Id)).Id); // same merge again → same answer

        var conflict = await api.PostAsJsonAsync($"/places/{a.Id}/merge", new MergePlaceRequest { IntoPlaceId = c.Id });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
    }

    [Fact]
    public async Task Merging_into_a_tombstone_follows_the_chain()
    {
        var api = Factory.ApiClient(Email);
        var a = await CreateAsync(api, "Survivor");
        var b = await CreateAsync(api, "First duplicate");
        var c = await CreateAsync(api, "Second duplicate");

        await MergeAsync(api, b.Id, a.Id);
        var merged = await MergeAsync(api, c.Id, b.Id); // b is a tombstone → lands on a
        Assert.Equal(a.Id, merged.Id);

        var redirected = (await api.GetFromJsonAsync<PlaceDto>($"/places/{c.Id}"))!;
        Assert.Equal(a.Id, redirected.Id);
    }

    [Fact]
    public async Task Self_merge_is_rejected()
    {
        var api = Factory.ApiClient(Email);
        var a = await CreateAsync(api, "Place A");
        var resp = await api.PostAsJsonAsync($"/places/{a.Id}/merge", new MergePlaceRequest { IntoPlaceId = a.Id });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }
}
