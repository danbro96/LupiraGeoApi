namespace LupiraGeoApi.Core.Domain;

/// <summary>Provenance of a gazetteer entry: created by a user (may be unverified), derived from geocoding, or imported from a gazetteer.</summary>
public enum PlaceSource { User, Geocoded, Imported }
