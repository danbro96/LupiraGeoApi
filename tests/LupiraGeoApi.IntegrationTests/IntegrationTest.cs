using Marten;
using LupiraGeoApi.Dtos.Me;
using System.Net.Http.Json;
using Xunit;

namespace LupiraGeoApi.IntegrationTests;

/// <summary>Base for integration tests: shares the container fixture, resets all state before each test. Serial within
/// the "integration" collection.</summary>
[Collection("integration")]
public abstract class IntegrationTest(GeoApiTestFactory factory) : IAsyncLifetime
{
    protected readonly GeoApiTestFactory Factory = factory;

    protected IDocumentStore Store => Factory.Store;

    public async Task InitializeAsync() => await Factory.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    protected static async Task<MeDto> GetMeAsync(HttpClient api) => (await api.GetFromJsonAsync<MeDto>("/me"))!;
    protected static async Task<Guid> GetMyIdAsync(HttpClient api) => (await GetMeAsync(api)).Id;
}
