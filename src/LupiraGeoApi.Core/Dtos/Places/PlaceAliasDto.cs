namespace LupiraGeoApi.Core.Dtos.Places;

public sealed class PlaceAliasDto
{
    public required Guid Id { get; set; }

    public required string Name { get; set; }

    public string? Lang { get; set; }
}
