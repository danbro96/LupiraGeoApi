namespace LupiraGeoApi.Core.Domain;

/// <summary>A reconciliation key to an external gazetteer, so imports and dedup can match a <see cref="Place"/> across sources.</summary>
public sealed class PlaceExternalId
{
    public Guid Id { get; set; }

    public Guid PlaceId { get; set; }

    public ExternalScheme Scheme { get; set; }

    public string Value { get; set; } = string.Empty;
}
