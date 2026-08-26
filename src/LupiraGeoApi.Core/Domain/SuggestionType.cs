namespace LupiraGeoApi.Core.Domain;

/// <summary>What a typeahead suggestion points at: a gazetteer <see cref="Place"/> or an <see cref="AdminArea"/> locality.</summary>
public enum SuggestionType
{
    Place,
    Locality,
}
