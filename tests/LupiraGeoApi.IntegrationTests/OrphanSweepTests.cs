using LupiraGeoApi.Core.Domain;
using LupiraGeoApi.Core.Dtos.Curation;
using LupiraGeoApi.Core.Dtos.Places;
using LupiraGeoApi.Core.Dtos.SavedPlaces;
using System.Net.Http.Json;
using System.Net;
using Xunit;

namespace LupiraGeoApi.IntegrationTests;

/// <summary>Own collection: the factory points ContactApi/CalendarApi at a <see cref="PlaceRefsStub"/>.</summary>
public sealed class OrphanSweepFixture : IAsyncLifetime
{
    public PlaceRefsStub Refs { get; private set; } = null!;
    public GeoApiTestFactory Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Refs = await PlaceRefsStub.StartAsync();
        Factory = new GeoApiTestFactory();
        Factory.ExtraConfig["ContactApi:BaseUrl"] = Refs.BaseUrl;
        Factory.ExtraConfig["ContactApi:DevUser"] = "geo-svc@x.test";
        Factory.ExtraConfig["CalendarApi:BaseUrl"] = Refs.BaseUrl;
        Factory.ExtraConfig["CalendarApi:DevUser"] = "geo-svc@x.test";
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await Refs.DisposeAsync();
    }
}

[CollectionDefinition("orphan-sweep")]
public sealed class OrphanSweepCollection : ICollectionFixture<OrphanSweepFixture>;

[Collection("orphan-sweep")]
public sealed class OrphanSweepTests(OrphanSweepFixture fx) : IAsyncLifetime
{
    const string Email = "alice@x.test";

    public Task InitializeAsync()
    {
        fx.Refs.ContactRefs.Clear();
        fx.Refs.CalendarRefs.Clear();
        fx.Refs.FailContacts = false;
        fx.Refs.FailCalendar = false;
        lock (fx.Refs.ContactRequests) fx.Refs.ContactRequests.Clear();
        lock (fx.Refs.CalendarRequests) fx.Refs.CalendarRequests.Clear();
        return fx.Factory.ResetAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<PlaceDto> CreateAsync(HttpClient api, string name)
    {
        var resp = await api.PostAsJsonAsync("/places", new CreatePlaceRequest { Name = name });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<PlaceDto>())!;
    }

    private async Task<List<OrphanCandidateDto>> FindAsync(HttpClient api)
    {
        var resp = await api.GetAsync("/curation/orphans");
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<List<OrphanCandidateDto>>())!;
    }

    private async Task<List<PrunePlaceResultDto>> PruneAsync(HttpClient api, params Guid[] ids)
    {
        var resp = await api.PostAsJsonAsync("/curation/prune", new PrunePlacesRequest { PlaceIds = [.. ids] });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<List<PrunePlaceResultDto>>())!;
    }

    [Fact]
    public async Task Referenced_places_are_excluded_and_zero_ref_places_are_prunable()
    {
        var api = fx.Factory.ApiClient(Email);
        var orphan = await CreateAsync(api, "Forgotten Bench");
        var byContact = await CreateAsync(api, "Grandma's House");
        var byCalendar = await CreateAsync(api, "Meeting Spot");
        var bySaved = await CreateAsync(api, "Fishing Lake");

        fx.Refs.ContactRefs[byContact.Id] = 1;
        fx.Refs.CalendarRefs[byCalendar.Id] = (2, 0);
        (await api.PostAsJsonAsync("/me/places", new CreateSavedPlaceRequest { Label = "Lake", PlaceId = bySaved.Id }))
            .EnsureSuccessStatusCode();

        var candidates = await FindAsync(api);

        var ids = candidates.Select(c => c.PlaceId).ToHashSet();
        Assert.Contains(orphan.Id, ids);
        Assert.DoesNotContain(byContact.Id, ids);
        Assert.DoesNotContain(byCalendar.Id, ids);
        Assert.DoesNotContain(bySaved.Id, ids);
        Assert.True(candidates.Single(c => c.PlaceId == orphan.Id).Prunable);
    }

    [Fact]
    public async Task A_reference_held_on_a_merged_loser_id_protects_the_survivor()
    {
        var api = fx.Factory.ApiClient(Email);
        var survivor = await CreateAsync(api, "Cafe Central");
        var loser = await CreateAsync(api, "Cafe Centralen");
        (await api.PostAsJsonAsync($"/places/{loser.Id}/merge", new MergePlaceRequest { IntoPlaceId = survivor.Id }))
            .EnsureSuccessStatusCode();

        fx.Refs.ContactRefs[loser.Id] = 1;   // a contact still holds the pre-merge id

        var candidates = await FindAsync(api);
        Assert.DoesNotContain(survivor.Id, candidates.Select(c => c.PlaceId));

        // The tombstone id was part of the check request — otherwise the loser ref could never be seen.
        List<Guid> lastRequest;
        lock (fx.Refs.ContactRequests) lastRequest = fx.Refs.ContactRequests.Last();
        Assert.Contains(loser.Id, lastRequest);
    }

    [Fact]
    public async Task Deleted_cal_only_references_flag_the_place_and_block_prune()
    {
        var api = fx.Factory.ApiClient(Email);
        var place = await CreateAsync(api, "Old Venue");
        fx.Refs.CalendarRefs[place.Id] = (0, 3);

        var candidate = Assert.Single(await FindAsync(api), c => c.PlaceId == place.Id);
        Assert.False(candidate.Prunable);
        Assert.Equal(3, candidate.CalendarDeletedRefs);

        var result = Assert.Single(await PruneAsync(api, place.Id));
        Assert.Equal(PruneStatus.Referenced, result.Status);
        Assert.NotNull(result.Reason);
    }

    [Fact]
    public async Task Prune_soft_deletes_and_is_idempotent()
    {
        var api = fx.Factory.ApiClient(Email);
        var place = await CreateAsync(api, "Forgotten Bench");

        var first = Assert.Single(await PruneAsync(api, place.Id));
        Assert.Equal(PruneStatus.Pruned, first.Status);
        Assert.Equal(HttpStatusCode.NotFound, (await api.GetAsync($"/places/{place.Id}")).StatusCode);

        var again = Assert.Single(await PruneAsync(api, place.Id));
        Assert.Equal(PruneStatus.Pruned, again.Status);
    }

    [Fact]
    public async Task A_reference_appearing_between_find_and_prune_blocks_the_prune()
    {
        var api = fx.Factory.ApiClient(Email);
        var place = await CreateAsync(api, "Suddenly Popular");
        Assert.Contains(place.Id, (await FindAsync(api)).Select(c => c.PlaceId));

        fx.Refs.ContactRefs[place.Id] = 1;   // referenced after the find

        var result = Assert.Single(await PruneAsync(api, place.Id));
        Assert.Equal(PruneStatus.Referenced, result.Status);
        Assert.Equal(HttpStatusCode.OK, (await api.GetAsync($"/places/{place.Id}")).StatusCode);
    }

    [Fact]
    public async Task Unknown_and_merged_ids_prune_as_not_found()
    {
        var api = fx.Factory.ApiClient(Email);
        var survivor = await CreateAsync(api, "Cafe Central");
        var loser = await CreateAsync(api, "Cafe Centralen");
        (await api.PostAsJsonAsync($"/places/{loser.Id}/merge", new MergePlaceRequest { IntoPlaceId = survivor.Id }))
            .EnsureSuccessStatusCode();

        var results = await PruneAsync(api, Guid.NewGuid(), loser.Id);
        Assert.All(results, r => Assert.Equal(PruneStatus.NotFound, r.Status));
    }

    [Fact]
    public async Task An_unreachable_reference_source_fails_closed()
    {
        var api = fx.Factory.ApiClient(Email);
        await CreateAsync(api, "Forgotten Bench");
        fx.Refs.FailCalendar = true;

        Assert.Equal(HttpStatusCode.BadRequest, (await api.GetAsync("/curation/orphans")).StatusCode);
    }
}

/// <summary>Against the shared factory (ContactApi/CalendarApi unset): the sweep must refuse, not report orphans.</summary>
public sealed class OrphanSweepUnconfiguredTests(GeoApiTestFactory factory) : IntegrationTest(factory)
{
    [Fact]
    public async Task Unconfigured_reference_sources_fail_closed()
    {
        var api = Factory.ApiClient("alice@x.test");
        Assert.Equal(HttpStatusCode.BadRequest, (await api.GetAsync("/curation/orphans")).StatusCode);
    }
}
