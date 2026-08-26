using System.Text.Json.Serialization;
using LupiraGeoApi.Core.Domain;

namespace LupiraGeoApi.Core.Dtos.Places;

/// <summary>Attach an external gazetteer id to a place. Multiple ids per scheme are allowed (e.g. an OSM <c>way/</c>
/// and <c>node/</c>). The <c>(Scheme, Value)</c> pair is globally unique — attaching one that already belongs to
/// another place is a conflict (merge those places instead).</summary>
public sealed class AddExternalIdRequest
{
    [JsonConverter(typeof(JsonStringEnumConverter<ExternalScheme>))]
    public required ExternalScheme Scheme { get; set; }
    public required string Value { get; set; }
}
