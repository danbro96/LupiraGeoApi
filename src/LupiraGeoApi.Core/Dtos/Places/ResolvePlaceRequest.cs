namespace LupiraGeoApi.Core.Dtos.Places;

/// <summary>Resolve free-text to a place id — match an existing entry, geocode, or provisionally create. This is what
/// LupiraCalApi calls when an item/travel-leg/contact address carries a location string.</summary>
public sealed class ResolvePlaceRequest
{
    public required string Text { get; set; }
}
