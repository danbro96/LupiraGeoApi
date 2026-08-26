using System.Net.Http.Json;
using LupiraGeoApi.Core.Domain;
using LupiraGeoApi.Core.Dtos.Geocoding;
using LupiraGeoApi.Core.Dtos.Places;
using Xunit;

namespace LupiraGeoApi.IntegrationTests;

[CollectionDefinition("geocoding")]
public sealed class GeocodingCollection : ICollectionFixture<GeocodingFixture>;
