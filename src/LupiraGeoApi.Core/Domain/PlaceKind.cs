namespace LupiraGeoApi.Core.Domain;

/// <summary>What a <see cref="Place"/> is: a point-of-interest (a named venue/entity), a plain street address, or a whole
/// settlement/administrative area (city, municipality, region, country) — a centroid, not somewhere you arrive.</summary>
public enum PlaceKind
{
    Poi,
    Address,
    Area,
}
