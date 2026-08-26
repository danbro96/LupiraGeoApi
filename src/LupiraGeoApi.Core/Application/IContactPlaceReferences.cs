namespace LupiraGeoApi.Core.Application;

/// <summary>How many contact addresses reference each of the given place ids (LupiraContactApi's
/// <c>/internal/contacts/place-references:check</c> seam). Null = the source is unreachable — the orphan sweep
/// must fail closed on it, never treat it as zero references.</summary>
public interface IContactPlaceReferences
{
    bool IsConfigured { get; }
    Task<IReadOnlyList<PlaceReferenceCount>?> CheckAsync(IReadOnlyList<Guid> placeIds, CancellationToken ct = default);
}
