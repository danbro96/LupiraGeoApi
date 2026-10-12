using LupiraGeoApi.Core.Application.Geocoding;
using LupiraGeoApi.Core.Application.Places;
using LupiraGeoApi.Core.Domain;
using Xunit;

namespace LupiraGeoApi.UnitTests;

public sealed class HouseNumberTests
{
    private static GeocodeHit Hit(string? houseNumber = null, bool coarse = false) =>
        new("x", 0, 0, PlaceCategory.Unknown, "SE", null, null, null, "way", 1, HouseNumber: houseNumber, IsCoarse: coarse);

    [Theory]
    [InlineData("Storgatan 5", "5")]
    [InlineData("Storgatan 5A", "5A")]
    [InlineData("Storgatan 5 B", "5B")]
    [InlineData("storgatan 5b", "5B")]
    [InlineData("Storgatan 5, lgh 1102", "5")]
    [InlineData("Storgatan 5 lgh 1102", "5")]
    [InlineData("lgh 1102, Storgatan 5", "5")]
    [InlineData("Drottninggatan 10, Stockholm", "10")]
    [InlineData("Drottninggatan 10 Stockholm", "10")]
    [InlineData("Sjätte Tunnan, Stora Nygatan 43, 111 27 Stockholm", "43")]
    [InlineData("Bergshamra T-Bana, Kraus väg 2, 170 77 Solna", "2")]
    [InlineData("Centreumhuset 2 Käppuddsgatan, 824 32 Hudiksvall", "2")]
    [InlineData("55 Liljeörnsgatan, 165 53 Hässelby, SE", "55")]
    [InlineData("5B Storgatan", "5B")]
    public void Finds_the_house_number(string text, string expected) =>
        Assert.Equal(expected, HouseNumber.Find(text));

    [Theory]
    [InlineData("Drottninggatan, Stockholm")]
    [InlineData("Bara enkelt")]
    [InlineData("Valla, 640 23")]
    [InlineData("E4, Södertälje")]
    [InlineData("Storgatan 12345")]
    [InlineData("111 45 Stockholm")]
    public void No_house_number(string text) =>
        Assert.Null(HouseNumber.Find(text));

    [Theory]
    [InlineData("Drottninggatan 10, Stockholm", "Drottninggatan 22, Stockholm", true)]
    [InlineData("Storgatan 5", "Storgatan 5A", true)]
    [InlineData("Storgatan 5 B", "Storgatan 5b, Lund", false)]
    [InlineData("Storgatan 5, lgh 1102", "Storgatan 5", false)]
    [InlineData("Bara enkelt", "Bara enkelt, Skånegatan 59", false)]
    public void Different_numbers_conflict(string a, string b, bool expected) =>
        Assert.Equal(expected, HouseNumber.Conflicts(a, b));

    [Fact]
    public void A_street_hit_for_a_numbered_address_is_approximate() =>
        Assert.True(HouseNumber.IsApproximateFix("Drottninggatan 22, Stockholm", Hit(coarse: true)));

    [Fact]
    public void A_hit_with_another_number_is_approximate() =>
        Assert.True(HouseNumber.IsApproximateFix("Drottninggatan 22, Stockholm", Hit("20")));

    [Theory]
    [InlineData("Drottninggatan 22", "22")]
    [InlineData("Drottninggatan 22A", "22 a")]
    public void A_hit_with_the_requested_number_is_exact(string requested, string number) =>
        Assert.False(HouseNumber.IsApproximateFix(requested, Hit(number)));

    [Fact]
    public void A_venue_hit_without_a_number_is_exact() =>
        Assert.False(HouseNumber.IsApproximateFix("Bara enkelt, Skånegatan 59", Hit()));

    [Fact]
    public void A_request_without_a_number_is_never_approximate() =>
        Assert.False(HouseNumber.IsApproximateFix("Drottninggatan, Stockholm", Hit(coarse: true)));

    [Fact]
    public void Parses_house_number_and_coarseness_from_nominatim()
    {
        const string payload = """
            [{"lat":"59.33","lon":"18.06","display_name":"Drottninggatan, Norrmalm, Stockholm","addresstype":"road",
              "category":"highway","type":"pedestrian","osm_type":"way","osm_id":4491949,
              "address":{"road":"Drottninggatan","city":"Stockholm","country_code":"se"}},
             {"lat":"59.33","lon":"18.06","display_name":"10, Drottninggatan, Stockholm","addresstype":"building",
              "category":"building","type":"yes","osm_type":"way","osm_id":1,
              "address":{"house_number":"10","road":"Drottninggatan","city":"Stockholm","country_code":"se"}}]
            """;
        var hits = GeocodingService.ParseCached(payload);
        Assert.True(hits[0].IsCoarse);
        Assert.Null(hits[0].HouseNumber);
        Assert.False(hits[1].IsCoarse);
        Assert.Equal("10", hits[1].HouseNumber);
    }
}
