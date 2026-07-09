using System.Net;
using System.Net.Http.Json;
using LupiraGeoApi.Dtos.SavedPlaces;
using Xunit;

namespace LupiraGeoApi.IntegrationTests;

/// <summary>Per-principal saved places: CRUD, favorites ordering, and cross-principal isolation.</summary>
public sealed class SavedPlacesTests(GeoApiTestFactory factory) : IntegrationTest(factory)
{
    private static async Task<SavedPlaceDto> CreateAsync(HttpClient api, string label, bool favorite = false)
    {
        var resp = await api.PostAsJsonAsync("/me/places", new CreateSavedPlaceRequest
        {
            Label = label, Latitude = 59.33, Longitude = 18.06, IsFavorite = favorite,
        });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<SavedPlaceDto>())!;
    }

    [Fact]
    public async Task Create_requires_a_place_or_coordinates()
    {
        var api = Factory.ApiClient("alice@x.test");
        var resp = await api.PostAsJsonAsync("/me/places", new CreateSavedPlaceRequest { Label = "Home" });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task List_returns_favorites_first()
    {
        var api = Factory.ApiClient("alice@x.test");
        await CreateAsync(api, "Work");
        await CreateAsync(api, "Home", favorite: true);

        var list = await api.GetFromJsonAsync<List<SavedPlaceDto>>("/me/places");
        Assert.Equal(2, list!.Count);
        Assert.Equal("Home", list[0].Label);   // favorite first
    }

    [Fact]
    public async Task Update_and_delete()
    {
        var api = Factory.ApiClient("alice@x.test");
        var created = await CreateAsync(api, "Gym");

        var upd = await api.PatchAsJsonAsync($"/me/places/{created.Id}", new UpdateSavedPlaceRequest { IsFavorite = true, Label = "The Gym" });
        upd.EnsureSuccessStatusCode();
        Assert.True((await upd.Content.ReadFromJsonAsync<SavedPlaceDto>())!.IsFavorite);

        var del = await api.DeleteAsync($"/me/places/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);
        Assert.Empty((await api.GetFromJsonAsync<List<SavedPlaceDto>>("/me/places"))!);
    }

    [Fact]
    public async Task Saved_places_are_isolated_per_principal()
    {
        var alice = Factory.ApiClient("alice@x.test");
        var bob = Factory.ApiClient("bob@x.test");
        var aliceSaved = await CreateAsync(alice, "Alice Home", favorite: true);

        Assert.Empty((await bob.GetFromJsonAsync<List<SavedPlaceDto>>("/me/places"))!);

        // Bob cannot mutate Alice's saved place — reads as not found.
        var upd = await bob.PatchAsJsonAsync($"/me/places/{aliceSaved.Id}", new UpdateSavedPlaceRequest { Label = "Hijacked" });
        Assert.Equal(HttpStatusCode.NotFound, upd.StatusCode);
    }
}
