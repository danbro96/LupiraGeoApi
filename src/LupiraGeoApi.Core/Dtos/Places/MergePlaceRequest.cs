namespace LupiraGeoApi.Core.Dtos.Places;

/// <summary>Merge the addressed place into <see cref="IntoPlaceId"/> (the survivor). The addressed id becomes a
/// tombstone redirect, so ids held by other services keep resolving.</summary>
public sealed class MergePlaceRequest
{
    public required Guid IntoPlaceId { get; set; }
}
