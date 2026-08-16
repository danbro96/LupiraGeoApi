using System.Text.Json.Serialization;
using LupiraGeoApi.Domain;

namespace LupiraGeoApi.Dtos.Curation;

/// <summary>A live place nothing references: zero contact addresses, zero live calendar items, zero saved places.
/// <see cref="Prunable"/> is false when soft-deleted calendar items still reference it — an undelete would dangle.</summary>
public sealed class OrphanCandidateDto
{
    public required Guid PlaceId { get; set; }
    public required string Name { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter<PlaceKind>))]
    public required PlaceKind Kind { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter<PlaceCategory>))]
    public required PlaceCategory Category { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter<PlaceSource>))]
    public required PlaceSource Source { get; set; }

    public required bool Verified { get; set; }
    public required bool HasCoordinates { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
    public required int ContactRefs { get; set; }
    public required int CalendarLiveRefs { get; set; }
    public required int CalendarDeletedRefs { get; set; }
    public required int SavedPlaceRefs { get; set; }
    public required bool Prunable { get; set; }
}

public sealed class PrunePlacesRequest
{
    public required List<Guid> PlaceIds { get; set; }
}

public sealed class PrunePlaceResultDto
{
    public required Guid PlaceId { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter<PruneStatus>))]
    public required PruneStatus Status { get; set; }

    public string? Reason { get; set; }
}
