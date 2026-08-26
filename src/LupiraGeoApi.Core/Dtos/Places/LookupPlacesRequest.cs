namespace LupiraGeoApi.Core.Dtos.Places;

/// <summary>Bulk get-by-ids (max 200) — hydrates place ids other services store (calendar items, contact addresses)
/// into coordinates in one call. Duplicates are allowed; responses align index-for-index with the input.</summary>
public sealed class LookupPlacesRequest
{
    public required List<Guid> Ids { get; set; }
}
