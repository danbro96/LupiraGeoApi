using LupiraGeoApi.Core.Application.Places;
using LupiraGeoApi.Core.Data;
using LupiraGeoApi.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using Xunit;

namespace LupiraGeoApi.IntegrationTests;

/// <summary>The read-only audit finds merges made under the old fill rule that left the loser's address and OSM id on
/// the survivor, and writes nothing.</summary>
public sealed class MergeOverwriteReportTests(GeoApiTestFactory factory) : IntegrationTest(factory)
{
    private const string LoserAddress = "Käppuddsgatan 1, Lidingö, Sweden";

    private static Place NewPlace(string name, double? lat, double? lon, string? address) => new()
    {
        Id = Guid.NewGuid(),
        CanonicalName = name,
        NormalizedName = name.ToLowerInvariant(),
        Location = lat is { } y && lon is { } x ? new Point(x, y) { SRID = 4326 } : null,
        FormattedAddress = address,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static CurationEvent Merged(Place loser, Place survivor) => new()
    {
        PlaceId = loser.Id,
        Action = CurationAction.Merged,
        At = DateTimeOffset.UtcNow,
        RelatedPlaceId = survivor.Id,
    };

    [Fact]
    public async Task Lists_a_survivor_still_carrying_the_losers_address_and_osm_id()
    {
        var survivor = NewPlace("Mulle Meck i Glada Hudik", 61.7278494, 17.1084592, LoserAddress);
        var loser = NewPlace("Mulle Meck", 59.3612, 18.1471, LoserAddress);
        loser.MergedIntoId = survivor.Id;
        var cleanSurvivor = NewPlace("Cafe Central", 59.3293, 18.0686, "Vasagatan 1, Stockholm");
        var cleanLoser = NewPlace("Cafe Central Annex", 59.3294, 18.0687, "Vasagatan 3, Stockholm");
        cleanLoser.MergedIntoId = cleanSurvivor.Id;

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GeoDbContext>();
            db.Places.AddRange(survivor, loser, cleanSurvivor, cleanLoser);
            db.PlaceExternalIds.Add(new PlaceExternalId { Id = Guid.NewGuid(), PlaceId = survivor.Id, Scheme = ExternalScheme.Osm, Value = "node/1375840275" });
            db.CurationLog.AddRange(Merged(loser, survivor), Merged(cleanLoser, cleanSurvivor));
            await db.SaveChangesAsync();
        }

        await using (var session = Store.LightweightSession())
        {
            session.Store(new GeocodeCache
            {
                Id = GeocodeCache.ForwardId("Mulle Meck"),
                Kind = "forward",
                Key = "Mulle Meck",
                Payload = $$"""[{"lat":"59.3612","lon":"18.1471","display_name":"{{LoserAddress}}","osm_type":"node","osm_id":1375840275}]""",
                ResolvedAt = DateTimeOffset.UtcNow,
            });
            await session.SaveChangesAsync();
        }

        long logCount;
        List<MergeOverwrite> findings;
        using (var scope = Factory.Services.CreateScope())
        {
            findings = await scope.ServiceProvider.GetRequiredService<MergeOverwriteReport>().FindAsync();
            logCount = await scope.ServiceProvider.GetRequiredService<GeoDbContext>().CurationLog.LongCountAsync();
        }

        var finding = Assert.Single(findings);
        Assert.Equal(loser.Id, finding.LoserId);
        Assert.Equal(survivor.Id, finding.SurvivorId);
        Assert.Equal(LoserAddress, finding.InheritedAddress);
        var osm = Assert.Single(finding.InheritedOsmIds);
        Assert.Equal(new InheritedOsmId("node/1375840275", InheritedOsmEvidence.LoserGeocodeFix), osm);
        Assert.Equal(["node/1375840275"], finding.OsmIds);
        Assert.Equal(2, logCount);
    }

    [Fact]
    public async Task A_later_regeocode_of_the_survivor_clears_the_finding()
    {
        var survivor = NewPlace("Survivor", null, null, LoserAddress);
        var loser = NewPlace("Loser", 59.3612, 18.1471, LoserAddress);
        loser.MergedIntoId = survivor.Id;

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GeoDbContext>();
        db.Places.AddRange(survivor, loser);
        db.CurationLog.Add(Merged(loser, survivor));
        await db.SaveChangesAsync();
        db.CurationLog.Add(new CurationEvent { PlaceId = survivor.Id, Action = CurationAction.Regeocoded, At = DateTimeOffset.UtcNow, Detail = LoserAddress });
        await db.SaveChangesAsync();

        Assert.Empty(await scope.ServiceProvider.GetRequiredService<MergeOverwriteReport>().FindAsync());
    }
}
