using System.Net.Http.Json;
using LupiraGeoApi.Core.Domain;
using LupiraGeoApi.Core.Dtos.Places;
using Xunit;

namespace LupiraGeoApi.IntegrationTests;

[CollectionDefinition("geocoder-down")]
public sealed class GeocoderDownCollection : ICollectionFixture<GeocoderDownFixture>;
