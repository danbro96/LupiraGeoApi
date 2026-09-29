using LupiraGeoApi.Core.Application.Places;
using Xunit;

namespace LupiraGeoApi.UnitTests;

public sealed class PlaceTextNormalizerTests
{
    [Theory]
    [InlineData("Sjätte Tunnan, Stora Nygatan 43, 111 27 Stockholm", "Sjätte Tunnan, Stora Nygatan 43, 111 27 Stockholm, Sweden")]
    [InlineData("Bromstensvägen 154, 163 57 Spånga", "Bromstensvägen 154, 163 57 Spånga, Sweden")]
    [InlineData("Sveduddsvägen 31, 139 74 Djurhamn, Sweden", "Sveduddsvägen 31, SE-139 74 Djurhamn, Sverige")]
    [InlineData("Centreumhuset 2 Käppuddsgatan, 824 32 Hudiksvall", "Centreumhuset 2 Käppuddsgatan, 824 32 Hudiksvall, SE")]
    [InlineData("Lerbo Lilla Navesta 1, 640 23 Valla", "  lerbo   lilla navesta 1,  640 23 VALLA,  Sweden ")]
    public void Spelling_variants_of_one_address_share_a_key(string a, string b) =>
        Assert.Equal(PlaceTextNormalizer.Key(a), PlaceTextNormalizer.Key(b));

    [Theory]
    [InlineData("Malmö", "Malmo")]
    [InlineData("Västerås", "Vasteras")]
    public void Diacritics_fold(string a, string b) =>
        Assert.Equal(PlaceTextNormalizer.Key(a), PlaceTextNormalizer.Key(b));

    [Theory]
    [InlineData("Lerbo Lilla Navesta 1, 640 23 Valla", "Lerbo Lilla Navesta 1, 641 62 Valla")]
    [InlineData("Storgatan 1, Lund", "Storgatan 2, Lund")]
    [InlineData("Berlin, Germany", "Berlin, Sweden")]
    public void Genuinely_different_places_keep_different_keys(string a, string b) =>
        Assert.NotEqual(PlaceTextNormalizer.Key(a), PlaceTextNormalizer.Key(b));

    [Fact]
    public void A_place_named_for_the_home_country_keeps_its_key() =>
        Assert.Equal("sweden", PlaceTextNormalizer.Key("Sweden"));

    [Fact]
    public void Only_trailing_country_tokens_are_stripped() =>
        Assert.Equal("sweden house stockholm", PlaceTextNormalizer.Key("Sweden House, Stockholm, Sverige"));

    [Theory]
    [InlineData("100%", "100")]
    [InlineData("Café_1", "cafe 1")]
    public void Like_wildcards_are_neutralized(string input, string expected) =>
        Assert.Equal(expected, PlaceTextNormalizer.Key(input));

    [Fact]
    public void Canonical_keeps_the_display_form() =>
        Assert.Equal("Bara enkelt, Skånegatan 59", PlaceTextNormalizer.Canonical("  Bara enkelt,   Skånegatan 59 "));
}
