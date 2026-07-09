namespace LupiraGeoApi.Dtos.SavedPlaces;

/// <summary>A caller's saved place / personal label. References a gazetteer place, or carries a raw coordinate.</summary>
public sealed class SavedPlaceDto
{
    public required Guid Id { get; set; }
    public Guid? PlaceId { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public required string Label { get; set; }
    public string? Icon { get; set; }
    public required bool IsFavorite { get; set; }
}

public sealed class CreateSavedPlaceRequest
{
    public required string Label { get; set; }
    public Guid? PlaceId { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? Icon { get; set; }
    public bool IsFavorite { get; set; }
}

/// <summary>Update a saved place. Omitted members are left unchanged.</summary>
public sealed class UpdateSavedPlaceRequest
{
    public string? Label { get; set; }
    public string? Icon { get; set; }
    public bool? IsFavorite { get; set; }
}
