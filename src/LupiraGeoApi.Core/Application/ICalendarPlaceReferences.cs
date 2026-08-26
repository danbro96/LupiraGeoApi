namespace LupiraGeoApi.Core.Application;

/// <summary>How many calendar items reference each of the given place ids, split live/soft-deleted
/// (LupiraCalApi's <c>/internal/items/place-references:check</c> seam). Null = the source is unreachable —
/// the orphan sweep must fail closed on it, never treat it as zero references.</summary>
public interface ICalendarPlaceReferences
{
    bool IsConfigured { get; }
    Task<IReadOnlyList<CalendarPlaceReference>?> CheckAsync(IReadOnlyList<Guid> placeIds, CancellationToken ct = default);
}
