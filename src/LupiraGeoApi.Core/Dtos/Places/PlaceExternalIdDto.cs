using System.Text.Json.Serialization;
using LupiraGeoApi.Core.Domain;

namespace LupiraGeoApi.Core.Dtos.Places;

public sealed class PlaceExternalIdDto
{
    [JsonConverter(typeof(JsonStringEnumConverter<ExternalScheme>))]
    public required ExternalScheme Scheme { get; set; }

    public required string Value { get; set; }
}
