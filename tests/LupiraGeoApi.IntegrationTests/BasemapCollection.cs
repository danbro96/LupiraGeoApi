using Xunit;

namespace LupiraGeoApi.IntegrationTests;

[CollectionDefinition("basemap")]
public sealed class BasemapCollection : ICollectionFixture<BasemapFixture>;
