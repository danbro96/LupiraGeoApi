namespace LupiraGeoApi.Core.Dtos.SavedPlaces;

public sealed class CreateSavedPlaceRequest
{
    public required string Label { get; set; }

    public Guid? PlaceId { get; set; }

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    public string? Notes { get; set; }

    public bool IsFavorite { get; set; }
}
