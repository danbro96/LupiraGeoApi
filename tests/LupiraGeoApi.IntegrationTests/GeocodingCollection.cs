using Xunit;

namespace LupiraGeoApi.IntegrationTests;

[CollectionDefinition("geocoding")]
public sealed class GeocodingCollection : ICollectionFixture<GeocodingFixture>;
