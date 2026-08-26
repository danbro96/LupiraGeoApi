using Xunit;

namespace LupiraGeoApi.IntegrationTests;

[CollectionDefinition("orphan-sweep")]
public sealed class OrphanSweepCollection : ICollectionFixture<OrphanSweepFixture>;
