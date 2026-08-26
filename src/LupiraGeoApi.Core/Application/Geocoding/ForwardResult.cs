namespace LupiraGeoApi.Core.Application.Geocoding;

public sealed record ForwardResult(GeocodeStatus Status, IReadOnlyList<GeocodeHit> Hits)
{
    public static readonly ForwardResult Empty = new(GeocodeStatus.Empty, []);
    public static readonly ForwardResult Unavailable = new(GeocodeStatus.Unavailable, []);
    public static ForwardResult FromHits(IReadOnlyList<GeocodeHit> hits) => new(GeocodeStatus.Ok, hits);
}
