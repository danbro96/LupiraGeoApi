namespace LupiraGeoApi.Core.Domain;

/// <summary>An alternate name for a <see cref="Place"/> (translation, colloquialism, former name). Enables "same place, different names".</summary>
public sealed class PlaceAlias
{
    public Guid Id { get; set; }

    public Guid PlaceId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Lang { get; set; }
}
