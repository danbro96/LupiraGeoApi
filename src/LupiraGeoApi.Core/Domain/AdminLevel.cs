namespace LupiraGeoApi.Core.Domain;

/// <summary>Level of an <see cref="AdminArea"/> in the containment tree (Locality → Region → Country).</summary>
public enum AdminLevel
{
    Country,
    Region,
    Locality,
}
