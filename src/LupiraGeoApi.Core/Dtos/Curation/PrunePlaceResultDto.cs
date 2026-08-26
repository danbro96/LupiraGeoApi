using System.Text.Json.Serialization;
using LupiraGeoApi.Core.Domain;

namespace LupiraGeoApi.Core.Dtos.Curation;

public sealed class PrunePlaceResultDto
{
    public required Guid PlaceId { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter<PruneStatus>))]
    public required PruneStatus Status { get; set; }

    public string? Reason { get; set; }
}
