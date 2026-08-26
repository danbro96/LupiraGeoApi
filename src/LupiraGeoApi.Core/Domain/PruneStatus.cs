namespace LupiraGeoApi.Core.Domain;

/// <summary>Per-place outcome of an orphan prune: soft-deleted (or already was), skipped because something still
/// references it, or the id is unknown/merged.</summary>
public enum PruneStatus
{
    Pruned,
    Referenced,
    NotFound,
}
