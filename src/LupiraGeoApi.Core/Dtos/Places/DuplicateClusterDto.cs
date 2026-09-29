using System.Text.Json.Serialization;

namespace LupiraGeoApi.Core.Dtos.Places;

public sealed class DuplicateClusterDto
{
    [JsonConverter(typeof(JsonStringEnumConverter<DuplicateReason>))]
    public required DuplicateReason Reason { get; set; }

    /// <summary>Greatest separation within the cluster; null when the cluster has no coordinates to compare.</summary>
    public double? SpreadM { get; set; }

    public required List<PlaceDto> Places { get; set; }
}
