namespace LupiraGeoApi.Core.Dtos.SavedPlaces;

/// <summary>Update a saved place. Omitted members are left unchanged. Re-point the target by passing EITHER
/// <c>PlaceId</c> (link a gazetteer place; clears any raw coordinate) OR <c>Latitude</c>+<c>Longitude</c> together
/// (set a raw coordinate; clears any link) — not both. There is no "clear to nothing": drop a link by re-pointing
/// to a raw coordinate.</summary>
public sealed class UpdateSavedPlaceRequest
{
    public string? Label { get; set; }
    public Guid? PlaceId { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? Icon { get; set; }
    public string? Notes { get; set; }
    public bool? IsFavorite { get; set; }
}
