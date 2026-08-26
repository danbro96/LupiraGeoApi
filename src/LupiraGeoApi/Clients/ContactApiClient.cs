using LupiraGeoApi.Core.Abstractions;
using Microsoft.Extensions.Options;

namespace LupiraGeoApi.Clients;

/// <summary>HTTP <see cref="IContactPlaceReferences"/> against LupiraContactApi's
/// <c>POST /internal/contacts/place-references:check</c>.</summary>
public sealed class ContactApiClient(HttpClient http, IOptions<ContactApiOptions> options, ILogger<ContactApiClient> logger)
    : InternalServiceClient(http, options.Value, logger), IContactPlaceReferences
{
    public async Task<IReadOnlyList<PlaceReferenceCount>?> CheckAsync(IReadOnlyList<Guid> placeIds, CancellationToken ct = default)
    {
        var refs = await CheckAsync<ContactRef>("internal/contacts/place-references:check", e => e.Places, placeIds, ct);
        return refs is null ? null : [.. refs.Select(r => new PlaceReferenceCount(r.PlaceId, r.Count))];
    }

    private sealed class ContactRef
    {
        public Guid PlaceId { get; set; }

        public int Count { get; set; }
    }
}
