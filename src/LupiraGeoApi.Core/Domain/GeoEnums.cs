namespace LupiraGeoApi.Domain;

/// <summary>What a <see cref="Place"/> is: a point-of-interest (a named venue/entity) or a plain street address.</summary>
public enum PlaceKind { Poi, Address }

/// <summary>Semantic type of a place — drives filtering and iconography. Kept vendor-neutral and coarse; extend as needed.</summary>
public enum PlaceCategory
{
    Unknown, Home, Office, Restaurant, Cafe, Bar, Store, Grocery, School, University,
    Clinic, Hospital, Pharmacy, Gym, Park, Airport, Station, BusStop, Hotel, Landmark,
    Government, Worship, Other,
}

/// <summary>Provenance of a gazetteer entry: created by a user (may be unverified), derived from geocoding, or imported from a gazetteer.</summary>
public enum PlaceSource { User, Geocoded, Imported }

/// <summary>External identity system a <see cref="PlaceExternalId"/> reconciles against.</summary>
public enum ExternalScheme { Osm, Wikidata, Google, Geonames }

/// <summary>Level of an <see cref="AdminArea"/> in the containment tree (Locality → Region → Country).</summary>
public enum AdminLevel { Country, Region, Locality }

/// <summary>What a typeahead suggestion points at: a gazetteer <see cref="Place"/> or an <see cref="AdminArea"/> locality.</summary>
public enum SuggestionType { Place, Locality }

/// <summary>A curation decision recorded on a <see cref="Place"/> in the append-only <c>curation_log</c>. Stable names —
/// they are the future event-type names, so never rename a member (add new ones; keep old ones readable).</summary>
public enum CurationAction { Created, Verified, Unverified, Renamed, Recategorized, AliasAdded, AliasRemoved, Merged, Regeocoded, Relocated, Deleted }

/// <summary>How <see cref="Place"/> resolution landed: an existing entry matched, a fresh geocode created one, a
/// coordinate-less provisional stub was created (address not found), or the geocoder was unreachable so nothing was
/// created — the last is retryable and must NOT be mistaken for "not found".</summary>
public enum PlaceResolution { Matched, Geocoded, Provisional, GeocodeUnavailable }
