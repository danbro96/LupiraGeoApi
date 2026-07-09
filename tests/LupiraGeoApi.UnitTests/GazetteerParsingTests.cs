using LupiraGeoApi.Application;
using Xunit;

namespace LupiraGeoApi.UnitTests;

/// <summary>The GeoNames tab-separated row parsers (pure).</summary>
public sealed class GazetteerParsingTests
{
    [Fact]
    public void ParseCountry_reads_iso_name_geonameid_and_skips_comments()
    {
        Assert.Null(GazetteerImporter.ParseCountry("# comment"));
        var row = new[] { "SE", "SWE", "752", "SW", "Sweden", "Stockholm", "449964", "10000000", "EU", ".se",
            "SEK", "Krona", "46", "###  ##", "", "sv", "2661886", "NO,FI", "" };
        var c = GazetteerImporter.ParseCountry(string.Join('\t', row));
        Assert.NotNull(c);
        Assert.Equal("SE", c!.Value.Iso);
        Assert.Equal("Sweden", c.Value.Name);
        Assert.Equal(2661886, c.Value.GeonamesId);
    }

    [Fact]
    public void ParseAdmin1_reads_code_name_geonameid()
    {
        var a = GazetteerImporter.ParseAdmin1("SE.26\tStockholm\tStockholm\t2673723");
        Assert.NotNull(a);
        Assert.Equal("SE.26", a!.Value.Code);
        Assert.Equal("Stockholm", a.Value.Name);
        Assert.Equal(2673723, a.Value.GeonamesId);
    }

    [Fact]
    public void ParseCity_reads_coords_country_and_admin1()
    {
        var f = new string[19];
        f[0] = "2673730"; f[1] = "Stockholm"; f[4] = "59.33258"; f[5] = "18.0649";
        f[8] = "SE"; f[10] = "26"; f[14] = "1515017";
        var city = GazetteerImporter.ParseCity(string.Join('\t', f));
        Assert.NotNull(city);
        Assert.Equal("Stockholm", city!.Value.Name);
        Assert.Equal(59.33258, city.Value.Lat, 5);
        Assert.Equal(18.0649, city.Value.Lon, 4);
        Assert.Equal("SE", city.Value.CountryCode);
        Assert.Equal("26", city.Value.Admin1);
    }
}
