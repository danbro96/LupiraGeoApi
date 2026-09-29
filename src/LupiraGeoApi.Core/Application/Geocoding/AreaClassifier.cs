namespace LupiraGeoApi.Core.Application.Geocoding;

/// <summary>Whether a Nominatim hit is a settlement or administrative area rather than a venue or address. Free text like
/// "Stockholm, Sweden" resolves to the city itself, and storing that as a <c>Poi</c> made city centroids indistinguishable
/// from real venues. Neighbourhoods and islands stay venues: they are small enough to be a destination.</summary>
public static class AreaClassifier
{
    private static readonly HashSet<string> AreaTypes = new(StringComparer.Ordinal)
    {
        "country", "state", "region", "province", "state_district", "county", "municipality",
        "city", "town", "village", "hamlet", "city_district", "district", "borough", "suburb",
    };

    /// <summary><paramref name="addressType"/> (jsonv2 <c>addresstype</c>) decides when present; older payloads without
    /// it fall back to the OSM class/type of the matched object.</summary>
    public static bool IsArea(string? addressType, string? osmClass, string? osmType) =>
        addressType is not null
            ? AreaTypes.Contains(addressType)
            : (osmClass == "boundary" && osmType == "administrative")
              || (osmClass == "place" && osmType is not null && AreaTypes.Contains(osmType));
}
