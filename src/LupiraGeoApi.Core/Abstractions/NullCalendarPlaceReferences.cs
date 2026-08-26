namespace LupiraGeoApi.Core.Abstractions;

public sealed class NullCalendarPlaceReferences : ICalendarPlaceReferences
{
    public bool IsConfigured => false;

    public Task<IReadOnlyList<CalendarPlaceReference>?> CheckAsync(IReadOnlyList<Guid> placeIds, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<CalendarPlaceReference>?>(null);
}
