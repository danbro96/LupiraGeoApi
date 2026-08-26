using Xunit;

namespace LupiraGeoApi.IntegrationTests;

[CollectionDefinition("geocoder-down")]
public sealed class GeocoderDownCollection : ICollectionFixture<GeocoderDownFixture>;
