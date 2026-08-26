namespace LupiraGeoApi.Core.Application;

public sealed record CalendarPlaceReference(Guid PlaceId, int LiveCount, int DeletedCount);

/// <summary>How many calendar items reference each of the given place ids, split live/soft-deleted
/// (LupiraCalApi's <c>/internal/items/place-references:check</c> seam). Null = the source is unreachable —
/// the orphan sweep must fail closed on it, never treat it as zero references.</summary>
public interface ICalendarPlaceReferences
{
    bool IsConfigured { get; }
    Task<IReadOnlyList<CalendarPlaceReference>?> CheckAsync(IReadOnlyList<Guid> placeIds, CancellationToken ct = default);
}

public sealed class NullCalendarPlaceReferences : ICalendarPlaceReferences
{
    public bool IsConfigured => false;
    public Task<IReadOnlyList<CalendarPlaceReference>?> CheckAsync(IReadOnlyList<Guid> placeIds, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<CalendarPlaceReference>?>(null);
}
