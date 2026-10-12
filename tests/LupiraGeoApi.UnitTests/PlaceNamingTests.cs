using LupiraGeoApi.Core.Application.Geocoding;
using LupiraGeoApi.Core.Application.Places;
using LupiraGeoApi.Core.Domain;
using Xunit;

namespace LupiraGeoApi.UnitTests;

public sealed class PlaceNamingTests
{
    private static GeocodeHit Hit(string? name = null, string? street = null, string? number = null, string? locality = null) =>
        new("Display, Rest", 59.3, 13.5, PlaceCategory.Unknown, "SE", null, null, locality, null, null,
            HouseNumber: number, Street: street, Name: name);

    [Fact]
    public void Address_is_named_street_number_locality() =>
        Assert.Equal("Storgatan 5, Karlstad", PlaceNaming.FromHit(Hit(street: "Storgatan", number: "5", locality: "Karlstad")));

    [Fact]
    public void Venue_keeps_its_name() =>
        Assert.Equal("Kafé Kvarnen", PlaceNaming.FromHit(Hit("Kafé Kvarnen", "Storgatan", "7", "Karlstad")));

    [Fact]
    public void Street_named_like_itself_gets_its_locality() =>
        Assert.Equal("Storgatan, Karlstad", PlaceNaming.FromHit(Hit("Storgatan", "Storgatan", locality: "Karlstad")));

    [Fact]
    public void Without_components_the_first_display_segment_names_it() =>
        Assert.Equal("Display", PlaceNaming.FromHit(Hit()));

    [Fact]
    public void Pin_is_named_from_the_reverse_address() =>
        Assert.Equal("Storgatan 5, Karlstad", PlaceNaming.ForPin(Hit("Kafé", "Storgatan", "5", "Karlstad"), 59.3, 13.5));

    [Fact]
    public void Pin_without_an_address_is_named_by_coordinates()
    {
        Assert.Equal("59.33260, 18.06490", PlaceNaming.ForPin(null, 59.3326, 18.0649));
        Assert.Equal("59.33260, 18.06490", PlaceNaming.ForPin(Hit(locality: "Stockholm"), 59.3326, 18.0649));
    }
}
