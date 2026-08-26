using NetTopologySuite.Geometries;

namespace LupiraGeoApi.Core.Domain;

/// <summary>
/// A real-world place in the shared gazetteer (EF Core, <c>geo</c> schema). Identity is a stable <see cref="Id"/>,
/// independent of the label: <see cref="CanonicalName"/> + <see cref="Aliases"/> carry names, <see cref="Location"/>
/// carries coordinates (<c>geography(Point,4326)</c>, null only for unverified user places), and
/// <see cref="WithinAreaId"/> anchors it in the <see cref="AdminArea"/> containment tree. <see cref="ExternalIds"/>
/// reconcile against OSM/Wikidata/etc. Personal labels ("Home") do NOT live here — see <see cref="SavedPlace"/>.
/// </summary>
public sealed class Place
{
    public Guid Id { get; set; }
    public string CanonicalName { get; set; } = "";
    public PlaceKind Kind { get; set; }
    public PlaceCategory Category { get; set; }

    /// <summary>WGS84 point (SRID 4326), stored as PostGIS <c>geography</c>. Null for a provisional user place with no fix yet.</summary>
    public Point? Location { get; set; }

    public Guid? WithinAreaId { get; set; }
    public AdminArea? WithinArea { get; set; }

    public string? FormattedAddress { get; set; }

    public PlaceSource Source { get; set; }
    public bool Verified { get; set; }
    public Guid? CreatedByPrincipalId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Tombstone redirect: set when this place was merged away. The row stays so ids held by other
    /// services keep resolving (reads follow the chain); search/resolve exclude tombstones.</summary>
    public Guid? MergedIntoId { get; set; }
    public Place? MergedInto { get; set; }

    /// <summary>Soft-delete tombstone: set when a bad entry is removed (e.g. a wrong geocode with no valid survivor
    /// to merge into). Unlike <see cref="MergedIntoId"/> it does NOT redirect — reads 404 and search/resolve exclude
    /// it. The row stays for the audit trail.</summary>
    public DateTimeOffset? DeletedAt { get; set; }

    public List<PlaceAlias> Aliases { get; set; } = [];
    public List<PlaceExternalId> ExternalIds { get; set; } = [];
}
