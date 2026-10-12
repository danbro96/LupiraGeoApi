using LupiraGeoApi.Clients;
using LupiraGeoApi.Core.Application.Geocoding;
using LupiraGeoApi.Core.Domain;
using Xunit;

namespace LupiraGeoApi.UnitTests;

public sealed class PhotonFeatureMappingTests
{
    private const string Features = """
        {"type":"FeatureCollection","features":[
          {"type":"Feature","geometry":{"type":"Point","coordinates":[13.5036,59.3793]},
           "properties":{"osm_type":"N","osm_id":2456,"osm_key":"place","osm_value":"house","type":"house",
             "housenumber":"5B","street":"Storgatan","postcode":"652 24","city":"Karlstad","state":"Värmlands län",
             "country":"Sverige","countrycode":"SE"}},
          {"type":"Feature","geometry":{"type":"Point","coordinates":[13.5,59.38]},
           "properties":{"osm_type":"W","osm_id":77,"osm_key":"amenity","osm_value":"cafe","type":"house",
             "name":"Kafé Kvarnen","street":"Storgatan","housenumber":"7","city":"Karlstad","countrycode":"SE"}},
          {"type":"Feature","geometry":{"type":"Point","coordinates":[13.511,59.3794]},
           "properties":{"osm_type":"R","osm_id":935,"osm_key":"place","osm_value":"city","type":"city",
             "name":"Karlstad","state":"Värmlands län","country":"Sverige","countrycode":"SE",
             "extent":[13.3,59.5,13.7,59.3]}},
          {"type":"Feature","geometry":{"type":"Point","coordinates":[13.50,59.37]},
           "properties":{"osm_type":"W","osm_id":4491,"osm_key":"highway","osm_value":"residential","type":"street",
             "name":"Storgatan","city":"Karlstad","countrycode":"SE"}},
          {"type":"Feature","geometry":{"type":"Point"},"properties":{}}
        ]}
        """;

    private static IReadOnlyList<GeocodeHit> Hits => PhotonAddressAutocomplete.ParseFeatures(Features);

    [Fact]
    public void House_maps_address_components_and_long_osm_type()
    {
        var house = Hits[0];
        Assert.Equal(("node", 2456L), (house.OsmType, house.OsmId!.Value));
        Assert.Equal(("Storgatan", "5B", "652 24", "Karlstad"), (house.Street, house.HouseNumber, house.Postcode, house.Locality));
        Assert.Equal(("SE", "Värmlands län"), (house.CountryCode, house.Region));
        Assert.Equal((59.3793, 13.5036), (house.Lat, house.Lon));
        Assert.False(house.IsCoarse);
        Assert.False(house.IsArea);
        Assert.Null(house.Name);
        Assert.Equal("Storgatan 5B, 652 24 Karlstad, Sverige", house.DisplayName);
    }

    [Fact]
    public void Venue_keeps_its_name_and_category()
    {
        var cafe = Hits[1];
        Assert.Equal(("way", "Kafé Kvarnen", PlaceCategory.Cafe), (cafe.OsmType, cafe.Name, cafe.Category));
        Assert.False(cafe.IsCoarse);
    }

    [Fact]
    public void City_is_a_coarse_area_and_its_own_locality()
    {
        var city = Hits[2];
        Assert.Equal("relation", city.OsmType);
        Assert.True(city.IsArea);
        Assert.True(city.IsCoarse);
        Assert.Equal("Karlstad", city.Locality);
    }

    [Fact]
    public void Street_is_coarse_but_not_an_area()
    {
        var street = Hits[3];
        Assert.Equal("Storgatan", street.Street);
        Assert.True(street.IsCoarse);
        Assert.False(street.IsArea);
    }

    [Fact]
    public void Feature_without_coordinates_is_skipped() => Assert.Equal(4, Hits.Count);

    [Fact]
    public void Free_text_goes_to_api_with_bias()
    {
        var path = PhotonAddressAutocomplete.PathAndQuery(AddressQuery.FromText("Storg"), new GeoPoint(59.38, 13.5), 5, null);
        Assert.Equal("/api?q=Storg&lat=59.38&lon=13.5&limit=5", path);
    }

    [Fact]
    public void Structured_splits_the_house_number()
    {
        var query = AddressQuery.Create(null, "Storgatan 5 B", "652 24", "Karlstad", "se").Value!;
        var path = PhotonAddressAutocomplete.PathAndQuery(query, null, 3, "sv");
        Assert.Equal("/structured?street=Storgatan&housenumber=5B&postcode=652%2024&city=Karlstad&countrycode=SE&limit=3&lang=sv", path);
    }

    [Theory]
    [InlineData("Storgatan 5", "Storgatan", "5")]
    [InlineData("Storgatan", "Storgatan", null)]
    [InlineData("Gamla vägen 12a", "Gamla vägen", "12a")]
    public void SplitStreet(string input, string street, string? number) =>
        Assert.Equal((street, number), PhotonAddressAutocomplete.SplitStreet(input));
}
