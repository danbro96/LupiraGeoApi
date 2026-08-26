namespace LupiraGeoApi.Core.Dtos.Places;

/// <summary><c>Place</c> is null when the id is unknown or soft-deleted; on a merge redirect it is the survivor
/// (<c>Place.Id != RequestedId</c>). Containment is omitted — <c>GET /places/{id}</c> remains the detail call.</summary>
public sealed class PlaceLookupItemDto
{
    public required Guid RequestedId { get; set; }
    public PlaceDto? Place { get; set; }
}
