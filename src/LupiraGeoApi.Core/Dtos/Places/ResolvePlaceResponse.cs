using System.Text.Json.Serialization;
using LupiraGeoApi.Core.Domain;

namespace LupiraGeoApi.Core.Dtos.Places;

public sealed class ResolvePlaceResponse
{
    [JsonConverter(typeof(JsonStringEnumConverter<PlaceResolution>))]
    public required PlaceResolution Resolution { get; set; }

    /// <summary>Null only when <see cref="Resolution"/> is <see cref="PlaceResolution.GeocodeUnavailable"/> — the
    /// geocoder was unreachable and nothing was created; the item is retryable.</summary>
    public Guid? PlaceId { get; set; }

    public required string Name { get; set; }

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }
}
