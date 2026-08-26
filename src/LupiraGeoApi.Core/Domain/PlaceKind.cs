namespace LupiraGeoApi.Core.Domain;

/// <summary>What a <see cref="Place"/> is: a point-of-interest (a named venue/entity) or a plain street address.</summary>
public enum PlaceKind
{
    Poi,
    Address,
}
