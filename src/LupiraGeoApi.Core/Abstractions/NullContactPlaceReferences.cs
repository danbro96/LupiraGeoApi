namespace LupiraGeoApi.Core.Abstractions;

public sealed class NullContactPlaceReferences : IContactPlaceReferences
{
    public bool IsConfigured => false;

    public Task<IReadOnlyList<PlaceReferenceCount>?> CheckAsync(IReadOnlyList<Guid> placeIds, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PlaceReferenceCount>?>(null);
}
