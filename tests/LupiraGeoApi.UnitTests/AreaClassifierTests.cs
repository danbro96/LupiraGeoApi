using LupiraGeoApi.Core.Application.Geocoding;
using Xunit;

namespace LupiraGeoApi.UnitTests;

public sealed class AreaClassifierTests
{
    [Theory]
    [InlineData("city", "boundary", "administrative")]
    [InlineData("municipality", "boundary", "administrative")]
    [InlineData("country", "boundary", "administrative")]
    [InlineData("town", "place", "town")]
    [InlineData("suburb", "boundary", "administrative")]
    public void Settlements_and_admin_areas_are_areas(string addressType, string osmClass, string osmType) =>
        Assert.True(AreaClassifier.IsArea(addressType, osmClass, osmType));

    [Theory]
    [InlineData("aeroway", "aeroway", "aerodrome")]
    [InlineData("railway", "railway", "station")]
    [InlineData("amenity", "amenity", "restaurant")]
    [InlineData("island", "place", "island")]
    [InlineData("neighbourhood", "place", "neighbourhood")]
    [InlineData("road", "highway", "residential")]
    public void Venues_islands_and_neighbourhoods_are_not(string addressType, string osmClass, string osmType) =>
        Assert.False(AreaClassifier.IsArea(addressType, osmClass, osmType));

    [Theory]
    [InlineData("boundary", "administrative", true)]
    [InlineData("place", "city", true)]
    [InlineData("place", "island", false)]
    [InlineData("tourism", "attraction", false)]
    public void Without_addresstype_the_osm_class_decides(string osmClass, string osmType, bool expected) =>
        Assert.Equal(expected, AreaClassifier.IsArea(null, osmClass, osmType));

    [Fact]
    public void Cached_city_payload_parses_as_an_area_hit()
    {
        const string payload = """
            [{"lat":"56.9494","lon":"24.1052","display_name":"Rīga, Latvija","addresstype":"city","category":"boundary",
              "type":"administrative","osm_type":"relation","osm_id":13048688,"address":{"country_code":"lv","city":"Rīga"}}]
            """;

        var hit = Assert.Single(GeocodingService.ParseCached(payload));

        Assert.True(hit.IsArea);
        Assert.Equal(("relation", 13048688L), (hit.OsmType, hit.OsmId));
    }

    [Fact]
    public void Malformed_payload_yields_no_hits() =>
        Assert.Empty(GeocodingService.ParseCached("{not json"));
}
