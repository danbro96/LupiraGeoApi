using NetTopologySuite.Geometries;

namespace LupiraGeoApi.Domain;

/// <summary>
/// An administrative region (EF Core, <c>geo</c> schema) — Country / Region / Locality — forming the containment tree
/// that <see cref="Place.WithinAreaId"/> hangs off. Read-only reference data seeded from GeoNames (see
/// <c>GazetteerImporter</c>); <see cref="GeonamesId"/> is the reconciliation key. <see cref="Boundary"/> polygons are
/// a future addition; for now only a <see cref="Centroid"/> is carried.
/// </summary>
public sealed class AdminArea
{
    public Guid Id { get; set; }
    public AdminLevel Level { get; set; }
    public string Name { get; set; } = "";

    /// <summary>ISO 3166-1 alpha-2 for a country; the GeoNames admin1 code for a region; null for a locality.</summary>
    public string? IsoCode { get; set; }

    public Guid? WithinAreaId { get; set; }
    public AdminArea? WithinArea { get; set; }

    /// <summary>Representative point (SRID 4326), stored as PostGIS <c>geography</c>.</summary>
    public Point? Centroid { get; set; }

    public long? GeonamesId { get; set; }
}
