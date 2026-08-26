namespace LupiraGeoApi.Core.Dtos.Places;

public sealed class AddAliasRequest
{
    public required string Name { get; set; }
    public string? Lang { get; set; }
}
