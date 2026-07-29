using LupiraGeoApi.Dtos.Places;
using LupiraGeoApi.Dtos.SavedPlaces;
using System.Net.Http.Json;
using System.Net;
using Xunit;

namespace LupiraGeoApi.IntegrationTests;

/// <summary>Per-principal saved places: CRUD, favorites ordering, coordinate/placeId re-point, and cross-principal isolation.</summary>
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

    private static async Task<PlaceDto> CreatePlaceAsync(HttpClient api, string name, double lat, double lon)
    {
        var resp = await api.PostAsJsonAsync("/places", new CreatePlaceRequest { Name = name, Latitude = lat, Longitude = lon });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<PlaceDto>())!;
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

        // Bob cannot mutate or delete Alice's saved place — reads as not found.
        var upd = await bob.PatchAsJsonAsync($"/me/places/{aliceSaved.Id}", new UpdateSavedPlaceRequest { Label = "Hijacked" });
        Assert.Equal(HttpStatusCode.NotFound, upd.StatusCode);
        var del = await bob.DeleteAsync($"/me/places/{aliceSaved.Id}");
        Assert.Equal(HttpStatusCode.NotFound, del.StatusCode);
    }

    [Fact]
    public async Task Update_corrects_raw_coordinates()
    {
        var api = Factory.ApiClient("alice@x.test");
        var pin = await CreateAsync(api, "Hemma");

        var upd = await api.PatchAsJsonAsync($"/me/places/{pin.Id}", new UpdateSavedPlaceRequest { Latitude = 59.382763, Longitude = 18.025224 });
        upd.EnsureSuccessStatusCode();
        var updated = (await upd.Content.ReadFromJsonAsync<SavedPlaceDto>())!;
        Assert.Equal(59.382763, updated.Latitude!.Value, 6);
        Assert.Equal(18.025224, updated.Longitude!.Value, 6);

        var got = (await api.GetFromJsonAsync<List<SavedPlaceDto>>("/me/places"))!.Single();
        Assert.Equal(59.382763, got.Latitude!.Value, 6);
    }

    [Fact]
    public async Task Update_repoints_raw_to_linked_and_resolves_coordinates()
    {
        var api = Factory.ApiClient("alice@x.test");
        var place = await CreatePlaceAsync(api, "Kungshamra 64a", 59.382763, 18.025224);
        var pin = await CreateAsync(api, "Hemma");   // raw 59.33/18.06

        var upd = await api.PatchAsJsonAsync($"/me/places/{pin.Id}", new UpdateSavedPlaceRequest { PlaceId = place.Id });
        upd.EnsureSuccessStatusCode();
        var updated = (await upd.Content.ReadFromJsonAsync<SavedPlaceDto>())!;
        Assert.Equal(place.Id, updated.PlaceId);
        Assert.Equal(59.382763, updated.Latitude!.Value, 6);   // resolved from the linked place
        Assert.Equal(18.025224, updated.Longitude!.Value, 6);
    }

    [Fact]
    public async Task Update_repoints_linked_to_raw()
    {
        var api = Factory.ApiClient("alice@x.test");
        var place = await CreatePlaceAsync(api, "Somewhere", 59.4, 18.1);
        var linked = (await (await api.PostAsJsonAsync("/me/places", new CreateSavedPlaceRequest { Label = "Linked", PlaceId = place.Id }))
            .Content.ReadFromJsonAsync<SavedPlaceDto>())!;

        var upd = await api.PatchAsJsonAsync($"/me/places/{linked.Id}", new UpdateSavedPlaceRequest { Latitude = 59.33, Longitude = 18.06 });
        upd.EnsureSuccessStatusCode();
        var updated = (await upd.Content.ReadFromJsonAsync<SavedPlaceDto>())!;
        Assert.Null(updated.PlaceId);
        Assert.Equal(59.33, updated.Latitude!.Value, 6);
    }

    [Fact]
    public async Task Update_rejects_both_placeId_and_coordinates()
    {
        var api = Factory.ApiClient("alice@x.test");
        var place = await CreatePlaceAsync(api, "Somewhere", 59.4, 18.1);
        var pin = await CreateAsync(api, "Hemma");

        var resp = await api.PatchAsJsonAsync($"/me/places/{pin.Id}", new UpdateSavedPlaceRequest { PlaceId = place.Id, Latitude = 59.33, Longitude = 18.06 });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Update_rejects_half_a_coordinate()
    {
        var api = Factory.ApiClient("alice@x.test");
        var pin = await CreateAsync(api, "Hemma");

        var resp = await api.PatchAsJsonAsync($"/me/places/{pin.Id}", new UpdateSavedPlaceRequest { Latitude = 59.33 });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task List_resolves_linked_place_coordinates()
    {
        var api = Factory.ApiClient("alice@x.test");
        var place = await CreatePlaceAsync(api, "Linked place", 59.382763, 18.025224);
        (await api.PostAsJsonAsync("/me/places", new CreateSavedPlaceRequest { Label = "Home", PlaceId = place.Id })).EnsureSuccessStatusCode();

        var listed = (await api.GetFromJsonAsync<List<SavedPlaceDto>>("/me/places"))!.Single();
        Assert.Equal(place.Id, listed.PlaceId);
        Assert.Equal(59.382763, listed.Latitude!.Value, 6);
        Assert.Equal(18.025224, listed.Longitude!.Value, 6);
    }
}
