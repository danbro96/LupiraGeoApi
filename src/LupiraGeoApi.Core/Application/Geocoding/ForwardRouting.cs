namespace LupiraGeoApi.Core.Application.Geocoding;

/// <summary>Which backends a forward geocode asks, in order; the first with hits answers.</summary>
public static class ForwardRouting
{
    public static IReadOnlyList<ForwardSource> Plan(GeocodeIntent intent, bool autocompleteConfigured) => intent switch
    {
        GeocodeIntent.Typeahead => autocompleteConfigured ? [ForwardSource.Autocomplete] : [ForwardSource.RegionalResolver],
        _ => autocompleteConfigured ? [ForwardSource.Resolver, ForwardSource.Autocomplete] : [ForwardSource.Resolver],
    };
}
