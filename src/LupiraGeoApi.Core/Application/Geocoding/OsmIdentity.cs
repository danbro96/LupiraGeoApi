namespace LupiraGeoApi.Core.Application.Geocoding;

/// <summary>OSM object types in both spellings: the long form stored in external ids (<c>way/123</c>) and the
/// single-letter form lookups take (<c>W123</c>).</summary>
public static class OsmIdentity
{
    public static char? Letter(string? osmType) => osmType?.Trim().ToLowerInvariant() switch
    {
        "n" or "node" => 'N',
        "w" or "way" => 'W',
        "r" or "relation" => 'R',
        _ => null,
    };

    public static string? TypeName(string? osmType) => Letter(osmType) switch
    {
        'N' => "node",
        'W' => "way",
        'R' => "relation",
        _ => null,
    };
}
