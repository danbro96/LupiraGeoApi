using System.Net.Http.Json;
using LupiraGeoApi.Core.Domain;
using LupiraGeoApi.Core.Dtos.Geocoding;
using Xunit;

namespace LupiraGeoApi.IntegrationTests;

/// <summary>A frozen empty forward answer holds for a day, then is re-asked; frozen hits never expire.</summary>
[Collection("geocoding")]
public sealed class EmptyAnswerExpiryTests(GeocodingFixture fx) : IAsyncLifetime
{
    const string Email = "alice@x.test";
    const string Query = "Shibuya Crossing";

    public Task InitializeAsync()
    {
        fx.Fallback.ReturnResults = true;
        return fx.Factory.ResetAsync();
    }

    public Task DisposeAsync()
    {
        fx.Fallback.ReturnResults = true;
        return Task.CompletedTask;
    }

    private Task<List<GeocodeResultDto>?> ForwardAsync() =>
        fx.Factory.ApiClient(Email).GetFromJsonAsync<List<GeocodeResultDto>>($"/geocode/forward?q={Uri.EscapeDataString(Query)}");

    private async Task AgeFrozenAnswerAsync(TimeSpan age)
    {
        await using var session = fx.Factory.Store.LightweightSession();
        var cached = (await session.LoadAsync<GeocodeCache>(GeocodeCache.ForwardId(Query)))!;
        cached.ResolvedAt = DateTimeOffset.UtcNow - age;
        session.Store(cached);
        await session.SaveChangesAsync();
    }

    [Fact]
    public async Task Fresh_empty_answer_is_served_from_the_cache()
    {
        fx.Fallback.ReturnResults = false;
        Assert.Empty((await ForwardAsync())!);

        fx.Fallback.ReturnResults = true;
        var f0 = fx.Fallback.SearchCalls;
        Assert.Empty((await ForwardAsync())!);
        Assert.Equal(f0, fx.Fallback.SearchCalls);
    }

    [Fact]
    public async Task Day_old_empty_answer_is_reasked()
    {
        fx.Fallback.ReturnResults = false;
        Assert.Empty((await ForwardAsync())!);
        await AgeFrozenAnswerAsync(TimeSpan.FromHours(25));

        fx.Fallback.ReturnResults = true;
        Assert.NotEmpty((await ForwardAsync())!);
    }

    [Fact]
    public async Task Old_hits_stay_frozen()
    {
        Assert.NotEmpty((await ForwardAsync())!);
        await AgeFrozenAnswerAsync(TimeSpan.FromDays(365));

        fx.Fallback.ReturnResults = false;
        var f0 = fx.Fallback.SearchCalls;
        Assert.NotEmpty((await ForwardAsync())!);
        Assert.Equal(f0, fx.Fallback.SearchCalls);
    }
}
