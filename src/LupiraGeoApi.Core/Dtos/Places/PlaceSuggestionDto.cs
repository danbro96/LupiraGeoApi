using System.Text.Json.Serialization;
using LupiraGeoApi.Core.Domain;

namespace LupiraGeoApi.Core.Dtos.Places;

/// <summary>A typeahead suggestion: a gazetteer place (name/alias trigram match) or an AdminArea locality — cities come
/// from the GeoNames seed, so they suggest without anyone having geocoded a POI there. <c>Context</c> disambiguates
/// (formatted address for places, parent area for localities).</summary>
public sealed class PlaceSuggestionDto
{
    public required Guid Id { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter<SuggestionType>))]
    public required SuggestionType Type { get; set; }

    public required string Name { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter<PlaceCategory>))]
    public PlaceCategory? Category { get; set; }

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    public string? Context { get; set; }
}
