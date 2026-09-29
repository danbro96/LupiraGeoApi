namespace LupiraGeoApi.Core.Domain;

/// <summary>A curation decision recorded on a <see cref="Place"/> in the append-only <c>curation_log</c>. Stable names —
/// they are the future event-type names, so never rename a member (add new ones; keep old ones readable).</summary>
public enum CurationAction
{
    Created,
    Verified,
    Unverified,
    Renamed,
    Recategorized,
    AliasAdded,
    AliasRemoved,
    Merged,
    Regeocoded,
    Relocated,
    Deleted,
    ExternalIdAdded,
    ExternalIdRemoved,
    Reclassified,
}
