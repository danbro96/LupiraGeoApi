namespace LupiraGeoApi.Core.Dtos.Curation;

public sealed class PrunePlacesRequest
{
    public required List<Guid> PlaceIds { get; set; }
}
