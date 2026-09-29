using LupiraGeoApi.Core.Application.Geocoding;
using LupiraGeoApi.Core.Domain;
using Xunit;

namespace LupiraGeoApi.UnitTests;

public sealed class ForwardHitFilterTests
{
    private static readonly HashSet<string> Nordic = new(["DK", "FI", "NO", "SE"], StringComparer.OrdinalIgnoreCase);

    private static GeocodeHit Hit(string? cc, double? importance = null, string? postcode = null) =>
        new("x", 0, 0, PlaceCategory.Unknown, cc, null, null, null, null, null, Postcode: postcode, Importance: importance);

    [Theory]
    [InlineData("582 15", false)] // Östra Valla, Linköping
    [InlineData("641 61", true)]  // Valla, Katrineholm
    [InlineData(null, true)]
    public void Postcode_region_must_match(string? hitPostcode, bool expected) =>
        Assert.Equal(expected, ForwardHitFilter.Accepts("640 23 Valla, Sweden", Hit("SE", postcode: hitPostcode), Nordic));

    [Fact]
    public void Unspaced_postcode_is_checked() =>
        Assert.False(ForwardHitFilter.Accepts("Kohlfurter Str. 46, 10999 Berlin", Hit("DE", postcode: "80331"), Nordic));

    [Theory]
    [InlineData("Berlin", "DK", 0.045, false)]
    [InlineData("Berlin", "DE", 0.85, true)]
    [InlineData("Simon", "FI", 0.147, false)]
    [InlineData("Simon", "RO", 0.42, false)]
    [InlineData("Herräng", "SE", 0.38, true)]
    public void Bare_word_needs_a_notable_hit(string query, string cc, double importance, bool expected) =>
        Assert.Equal(expected, ForwardHitFilter.Accepts(query, Hit(cc, importance), Nordic));

    [Fact]
    public void Bare_word_without_importance_is_rejected() =>
        Assert.False(ForwardHitFilter.Accepts("Herräng", Hit("SE"), Nordic));

    [Fact]
    public void Qualified_query_skips_the_notability_bar() =>
        Assert.True(ForwardHitFilter.Accepts("Berlin, Denmark", Hit("DK", 0.045), Nordic));

    [Fact]
    public void Without_regional_countries_every_bare_word_meets_the_foreign_bar() =>
        Assert.False(ForwardHitFilter.Accepts("Herräng", Hit("SE", 0.38), new HashSet<string>()));

    [Fact]
    public void Cached_payload_carries_postcode_and_importance()
    {
        const string payload = """
            [{"lat":"59.02","lon":"16.38","display_name":"Valla","importance":0.1467,"osm_type":"node","osm_id":365758733,
              "address":{"country_code":"se","postcode":"641 61"}}]
            """;

        var hit = Assert.Single(GeocodingService.ParseCached(payload));

        Assert.Equal(("641 61", 0.1467), (hit.Postcode, hit.Importance));
    }
}
