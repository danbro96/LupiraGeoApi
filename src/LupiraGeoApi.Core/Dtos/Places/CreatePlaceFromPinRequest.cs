using LupiraGeoApi.Core.Domain;

namespace LupiraGeoApi.Core.Dtos.Places;

/// <summary>Create (or match within 25 m) a place at a dropped pin. The location is the pin itself.</summary>
public sealed class CreatePlaceFromPinRequest
{
    /// <summary>Defaults to the address at the pin ("Street HouseNumber, Locality"), else its coordinates.</summary>
    public string? Name { get; set; }

    public required double Latitude { get; set; }

    public required double Longitude { get; set; }

    public PlaceCategory? Category { get; set; }
}
