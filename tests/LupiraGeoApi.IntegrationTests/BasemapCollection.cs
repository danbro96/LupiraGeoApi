using System.Net;
using System.Net.Http.Headers;
using LupiraGeoApi.Endpoints;
using Xunit;

namespace LupiraGeoApi.IntegrationTests;

[CollectionDefinition("basemap")]
public sealed class BasemapCollection : ICollectionFixture<BasemapFixture>;
