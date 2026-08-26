using LupiraGeoApi.Core.Application;
using Microsoft.Extensions.Options;

namespace LupiraGeoApi.Clients;

/// <summary>HTTP <see cref="ICalendarPlaceReferences"/> against LupiraCalApi's
/// <c>POST /internal/items/place-references:check</c>.</summary>
public sealed class CalendarApiClient(HttpClient http, IOptions<CalendarApiOptions> options, ILogger<CalendarApiClient> logger)
    : InternalServiceClient(http, options.Value, logger), ICalendarPlaceReferences
{
    public async Task<IReadOnlyList<CalendarPlaceReference>?> CheckAsync(IReadOnlyList<Guid> placeIds, CancellationToken ct = default)
    {
        var refs = await CheckAsync<ItemRef>("internal/items/place-references:check", e => e.Places, placeIds, ct);
        return refs is null ? null : [.. refs.Select(r => new CalendarPlaceReference(r.PlaceId, r.LiveCount, r.DeletedCount))];
    }

    private sealed class ItemRef
    {
        public Guid PlaceId { get; set; }
        public int LiveCount { get; set; }
        public int DeletedCount { get; set; }
    }
}
