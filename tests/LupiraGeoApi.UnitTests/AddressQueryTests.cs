using LupiraGeoApi.Core.Application.Geocoding;
using LupiraGeoApi.Core.Domain;
using Xunit;

namespace LupiraGeoApi.UnitTests;

public sealed class AddressQueryTests
{
    [Theory]
    [InlineData("Storgatan 5", null, null, null, null, true)]
    [InlineData(null, "Storgatan 5", null, null, null, true)]
    [InlineData(null, null, "652 24", "Karlstad", null, true)]
    [InlineData(null, null, "652 24", null, "SE", false)]
    [InlineData(null, null, null, "Karlstad", null, false)]
    [InlineData("Storgatan 5", "Storgatan 5", null, null, null, false)]
    [InlineData(" ", null, null, null, null, false)]
    [InlineData(null, "Storgatan 5", null, null, "SWE", false)]
    public void Validation(string? q, string? street, string? postcode, string? locality, string? cc, bool ok) =>
        Assert.Equal(ok, AddressQuery.Create(q, street, postcode, locality, cc).IsOk);

    [Fact]
    public void Text_keeps_the_existing_cache_key() =>
        Assert.Equal(GeocodeCache.ForwardId("Storgatan 5"), AddressQuery.Create(" Storgatan 5 ", null, null, null, null).Value!.CacheId);

    [Fact]
    public void Structured_cache_key_is_normalized_and_distinct_from_text()
    {
        var a = AddressQuery.Create(null, "Storgatan 5", "652 24", "Karlstad", "se").Value!;
        var b = AddressQuery.Create(null, " storgatan 5", "65224", "KARLSTAD ", "SE").Value!;
        var other = AddressQuery.Create(null, "Storgatan 7", "65224", "Karlstad", "SE").Value!;
        Assert.Equal(a.CacheId, b.CacheId);
        Assert.NotEqual(a.CacheId, other.CacheId);
        Assert.NotEqual(GeocodeCache.ForwardId("Storgatan 5"), a.CacheId);
    }

    [Theory]
    [InlineData("652 24", "652 30", true)]
    [InlineData("652 24", "582 15", false)]
    [InlineData("65224", null, true)]
    public void Structured_postcode_filters_hits(string wanted, string? got, bool expected)
    {
        var query = AddressQuery.Create(null, "Storgatan 5", wanted, null, null).Value!;
        var hit = new GeocodeHit("x", 0, 0, PlaceCategory.Unknown, "SE", null, null, null, null, null, Postcode: got);
        Assert.Equal(expected, ForwardHitFilter.MatchesPostcode(query, hit));
        Assert.Equal(expected, ForwardHitFilter.Accepts(query, hit, new HashSet<string>()));
    }

    [Fact]
    public void Structured_query_skips_the_bare_word_bar()
    {
        var query = AddressQuery.Create(null, "Storgatan", null, null, null).Value!;
        var hit = new GeocodeHit("x", 0, 0, PlaceCategory.Unknown, "SE", null, null, null, null, null);
        Assert.True(ForwardHitFilter.Accepts(query, hit, new HashSet<string>()));
    }
}
