namespace LupiraGeoApi.Core.Domain;

/// <summary>Semantic type of a place — drives filtering and iconography. Kept vendor-neutral and coarse; extend as needed.</summary>
public enum PlaceCategory
{
    Unknown,
    Home,
    Office,
    Restaurant,
    Cafe,
    Bar,
    Store,
    Grocery,
    School,
    University,
    Clinic,
    Hospital,
    Pharmacy,
    Gym,
    Park,
    Airport,
    Station,
    BusStop,
    Hotel,
    Landmark,
    Government,
    Worship,
    Other,
}
