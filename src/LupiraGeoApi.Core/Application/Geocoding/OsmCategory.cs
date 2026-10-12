using LupiraGeoApi.Core.Domain;

namespace LupiraGeoApi.Core.Application.Geocoding;

/// <summary>Best-effort OSM value/key (Nominatim type/class) → coarse <see cref="PlaceCategory"/>.</summary>
public static class OsmCategory
{
    public static PlaceCategory Map(string? type, string? klass) => type switch
    {
        "restaurant" or "fast_food" => PlaceCategory.Restaurant,
        "cafe" => PlaceCategory.Cafe,
        "bar" or "pub" or "nightclub" => PlaceCategory.Bar,
        "supermarket" or "convenience" or "grocery" => PlaceCategory.Grocery,
        "school" or "kindergarten" or "college" => PlaceCategory.School,
        "university" => PlaceCategory.University,
        "hospital" => PlaceCategory.Hospital,
        "clinic" or "doctors" or "dentist" => PlaceCategory.Clinic,
        "pharmacy" => PlaceCategory.Pharmacy,
        "gym" or "fitness_centre" or "sports_centre" => PlaceCategory.Gym,
        "park" or "garden" => PlaceCategory.Park,
        "aerodrome" or "airport" => PlaceCategory.Airport,
        "station" or "halt" or "subway_entrance" => PlaceCategory.Station,
        "bus_stop" => PlaceCategory.BusStop,
        "hotel" or "hostel" or "guest_house" => PlaceCategory.Hotel,
        "hotel " => PlaceCategory.Hotel,
        "attraction" or "monument" or "memorial" or "artwork" => PlaceCategory.Landmark,
        "townhall" or "government" => PlaceCategory.Government,
        "place_of_worship" => PlaceCategory.Worship,
        _ => klass switch
        {
            "shop" => PlaceCategory.Store,
            "tourism" => PlaceCategory.Landmark,
            "office" => PlaceCategory.Office,
            _ => PlaceCategory.Unknown,
        },
    };
}
