using Lupira.Testing.Mcp;
using Lupira.Testing.Postgres;
using Xunit;

namespace LupiraGeoApi.IntegrationTests;

[Collection("integration")]
public sealed class McpAuthDiscoveryTests(GeoApiTestFactory factory) : McpResourceMetadataTests
{
    protected override HttpClient CreateAnonymousClient() => factory.AnonymousClient();

    protected override string Issuer => factory.Authority!;

    protected override string RestProbePath => "/places";

    public override Task InitializeAsync() => factory.ResetAsync();
}
