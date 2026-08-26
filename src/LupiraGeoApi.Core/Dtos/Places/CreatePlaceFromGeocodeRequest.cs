namespace LupiraGeoApi.Core.Dtos.Places;

/// <summary>Create/dedupe a place from one specific forward-geocode hit the user picked, identified by OSM identity.
/// The hit must be among the (cached) geocode results for <see cref="Query"/> — the preview call froze them, so this
/// costs no extra geocoder round trip.</summary>
public sealed class CreatePlaceFromGeocodeRequest
{
    public required string Query { get; set; }
    public required string OsmType { get; set; }
    public required long OsmId { get; set; }

    /// <summary>Canonical name override; defaults to the normalized query text.</summary>
    public string? Name { get; set; }
}
