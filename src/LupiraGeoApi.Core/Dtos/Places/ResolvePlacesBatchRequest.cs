namespace LupiraGeoApi.Core.Dtos.Places;

/// <summary>Bulk <see cref="ResolvePlaceRequest"/> — for imports. Responses align index-for-index with the input.</summary>
public sealed class ResolvePlacesBatchRequest
{
    public required List<string> Texts { get; set; }
}
