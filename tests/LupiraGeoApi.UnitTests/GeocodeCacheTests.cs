using LupiraGeoApi.Domain;
using Xunit;

namespace LupiraGeoApi.UnitTests;

public sealed class GeocodeCacheTests
{
    [Fact]
    public void Quantize_snaps_to_a_100m_grid()
    {
        var (lat, lon) = GeocodeCache.Quantize(59.32931, 18.06861);
        Assert.Equal(59.329, lat, 3);
        Assert.Equal(18.069, lon, 3);
    }

    [Fact]
    public void ReverseId_is_stable_within_a_cell_and_distinct_across_cells()
    {
        var a = GeocodeCache.ReverseId(59.32931, 18.06841);
        var b = GeocodeCache.ReverseId(59.32949, 18.06849);   // same ~100 m cell (both round to 59.329, 18.068)
        var c = GeocodeCache.ReverseId(59.34000, 18.07000);   // different cell
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void ForwardId_is_case_insensitive_and_trimmed()
    {
        Assert.Equal(GeocodeCache.ForwardId("Cafe Central"), GeocodeCache.ForwardId("  cafe central  "));
        Assert.NotEqual(GeocodeCache.ForwardId("Cafe Central"), GeocodeCache.ForwardId("Cafe West"));
    }
}
