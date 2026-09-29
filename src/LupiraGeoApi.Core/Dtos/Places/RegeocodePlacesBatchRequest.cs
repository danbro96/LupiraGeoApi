namespace LupiraGeoApi.Core.Dtos.Places;

public sealed class RegeocodePlacesBatchRequest
{
    public required List<Guid> PlaceIds { get; set; }

    public bool Force { get; set; }
}
