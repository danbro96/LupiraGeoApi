using System.Text.Json.Serialization;

namespace LupiraGeoApi.Core.Dtos.Places;

public sealed class RegeocodePlaceResultDto
{
    public required Guid PlaceId { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter<RegeocodeStatus>))]
    public required RegeocodeStatus Status { get; set; }

    public string? Name { get; set; }

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    public string? Error { get; set; }
}
