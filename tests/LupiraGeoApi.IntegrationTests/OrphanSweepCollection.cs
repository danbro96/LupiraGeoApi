using System.Net;
using System.Net.Http.Json;
using LupiraGeoApi.Core.Domain;
using LupiraGeoApi.Core.Dtos.Curation;
using LupiraGeoApi.Core.Dtos.Places;
using LupiraGeoApi.Core.Dtos.SavedPlaces;
using Xunit;

namespace LupiraGeoApi.IntegrationTests;

[CollectionDefinition("orphan-sweep")]
public sealed class OrphanSweepCollection : ICollectionFixture<OrphanSweepFixture>;
