using System.Net;
using System.Net.Http.Json;
using Lupira.Testing.Postgres;
using LupiraGeoApi.Core.Domain;
using LupiraGeoApi.Core.Dtos.Places;
using Xunit;

namespace LupiraGeoApi.IntegrationTests;

/// <summary>GET /places/{id}/history — the append-only curation log, oldest first, readable after deletion.</summary>
public sealed class PlaceHistoryTests(GeoApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";

    [Fact]
    public async Task History_lists_actions_in_sequence_order_with_details()
    {
        var api = Factory.ApiClient(Email);
        var created = (await (await api.PostAsJsonAsync("/places", new CreatePlaceRequest { Name = "Cafe Central" }))
            .Content.ReadFromJsonAsync<PlaceDto>())!;
        (await api.PatchAsJsonAsync($"/places/{created.Id}", new UpdatePlaceRequest { Name = "Cafe Centralen" })).EnsureSuccessStatusCode();
        (await api.PatchAsJsonAsync($"/places/{created.Id}", new UpdatePlaceRequest { Verified = true })).EnsureSuccessStatusCode();

        var history = (await api.GetFromJsonAsync<List<CurationEventDto>>($"/places/{created.Id}/history"))!;

        Assert.Equal([CurationAction.Created, CurationAction.Renamed, CurationAction.Verified],
            history.Select(h => h.Action).ToArray());
        Assert.True(history.Zip(history.Skip(1)).All(p => p.First.Seq < p.Second.Seq));
        Assert.Equal("Cafe Centralen", history[1].Detail);
    }

    [Fact]
    public async Task History_stays_readable_after_soft_delete()
    {
        var api = Factory.ApiClient(Email);
        var created = (await (await api.PostAsJsonAsync("/places", new CreatePlaceRequest { Name = "Doomed" }))
            .Content.ReadFromJsonAsync<PlaceDto>())!;
        (await api.DeleteAsync($"/places/{created.Id}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await api.GetAsync($"/places/{created.Id}")).StatusCode);

        var history = (await api.GetFromJsonAsync<List<CurationEventDto>>($"/places/{created.Id}/history"))!;
        Assert.Equal([CurationAction.Created, CurationAction.Deleted], history.Select(h => h.Action).ToArray());
    }

    [Fact]
    public async Task Unknown_place_is_404()
    {
        var api = Factory.ApiClient(Email);
        Assert.Equal(HttpStatusCode.NotFound, (await api.GetAsync($"/places/{Guid.NewGuid()}/history")).StatusCode);
    }
}
