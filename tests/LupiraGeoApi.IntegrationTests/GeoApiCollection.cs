using Xunit;

namespace LupiraGeoApi.IntegrationTests;

/// <summary>One ephemeral Postgres container shared across the run; tests run serially (shared DB, reset per test).</summary>
[CollectionDefinition("integration")]
public sealed class GeoApiCollection : ICollectionFixture<GeoApiTestFactory>;
