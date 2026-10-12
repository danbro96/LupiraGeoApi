namespace LupiraGeoApi.Core.Dtos.Places;

/// <summary>Create/dedupe a place from one geocode hit the user picked, identified by OSM identity. Resolved from the
/// frozen results of <see cref="Query"/> when given, else by an OSM lookup; when the lookup misses, the hit's own
/// coordinates and address components (as shown to the user) create an approximate place.</summary>
public sealed class CreatePlaceFromGeocodeRequest
{
    /// <summary><c>node</c>/<c>way</c>/<c>relation</c>, or <c>N</c>/<c>W</c>/<c>R</c>.</summary>
    public required string OsmType { get; set; }

    public required long OsmId { get; set; }

    /// <summary>Canonical name override; defaults to the hit's name or "Street HouseNumber, Locality".</summary>
    public string? Name { get; set; }

    /// <summary>The search text the hit came from, when it was a cached search.</summary>
    public string? Query { get; set; }

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    public string? Street { get; set; }

    public string? HouseNumber { get; set; }

    public string? Postcode { get; set; }

    public string? Locality { get; set; }

    public string? CountryCode { get; set; }
}
