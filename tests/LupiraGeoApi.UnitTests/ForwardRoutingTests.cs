using LupiraGeoApi.Core.Application.Geocoding;
using Xunit;

namespace LupiraGeoApi.UnitTests;

public sealed class ForwardRoutingTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Typeahead_never_reaches_the_cached_public_chain(bool autocomplete) =>
        Assert.DoesNotContain(ForwardSource.Resolver, ForwardRouting.Plan(GeocodeIntent.Typeahead, autocomplete));

    [Fact]
    public void Typeahead_uses_autocomplete_when_configured() =>
        Assert.Equal([ForwardSource.Autocomplete], ForwardRouting.Plan(GeocodeIntent.Typeahead, true));

    [Fact]
    public void Typeahead_without_autocomplete_asks_the_regional_instance_only() =>
        Assert.Equal([ForwardSource.RegionalResolver], ForwardRouting.Plan(GeocodeIntent.Typeahead, false));

    [Fact]
    public void Search_resolves_first_and_falls_back_to_autocomplete() =>
        Assert.Equal([ForwardSource.Resolver, ForwardSource.Autocomplete], ForwardRouting.Plan(GeocodeIntent.Search, true));

    [Fact]
    public void Search_without_autocomplete_resolves_only() =>
        Assert.Equal([ForwardSource.Resolver], ForwardRouting.Plan(GeocodeIntent.Search, false));
}
