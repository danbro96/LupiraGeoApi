using System.Text.Json.Serialization;
using LupiraGeoApi.Domain;

namespace LupiraGeoApi.Dtos.Places;

/// <summary>One curation-log entry for a place. Readable for tombstoned/merged places too — that is the audit's point.</summary>
public sealed class CurationEventDto
{
    public required long Seq { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter<CurationAction>))]
    public required CurationAction Action { get; set; }

    public Guid? ActorPrincipalId { get; set; }
    public required DateTimeOffset At { get; set; }
    public Guid? RelatedPlaceId { get; set; }
    public string? Detail { get; set; }
}
