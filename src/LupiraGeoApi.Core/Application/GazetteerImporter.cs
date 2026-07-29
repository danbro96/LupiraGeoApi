using LupiraGeoApi.Data;
using LupiraGeoApi.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using System.Globalization;
using System.IO.Compression;

namespace LupiraGeoApi.Application;

public sealed record GazetteerImportResult(int Countries, int Regions, int Localities);

/// <summary>
/// Seeds the administrative reference tree from GeoNames (the <c>--seed-gazetteer</c> one-shot): countries
/// (<c>countryInfo.txt</c>), regions (<c>admin1CodesASCII.txt</c>), and localities (<c>cities500.zip</c>). Idempotent —
/// keyed by <see cref="AdminArea.GeonamesId"/>, existing rows are skipped, so re-running only tops up. Downloads from
/// <c>Geonames:BaseUrl</c> (default the public GeoNames dump). Data is licensed CC BY 4.0 (attribution in the README);
/// files are never committed.
/// </summary>
public sealed class GazetteerImporter(GeoDbContext db, IConfiguration config, ILogger<GazetteerImporter> logger)
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

    private string BaseUrl => (config["Geonames:BaseUrl"] is { Length: > 0 } b ? b : "https://download.geonames.org/export/dump").TrimEnd('/');

    public async Task<GazetteerImportResult> ImportAsync(CancellationToken ct = default)
    {
        var countries = await ImportCountriesAsync(ct);
        var byIso = await db.AdminAreas.Where(a => a.Level == AdminLevel.Country)
            .ToDictionaryAsync(a => a.IsoCode!, a => a.Id, ct);

        var regions = await ImportRegionsAsync(byIso, ct);
        var byAdmin1 = await db.AdminAreas.Where(a => a.Level == AdminLevel.Region && a.IsoCode != null)
            .ToDictionaryAsync(a => a.IsoCode!, a => a.Id, ct);

        var localities = await ImportLocalitiesAsync(byIso, byAdmin1, ct);
        logger.LogInformation("Gazetteer import: {Countries} countries, {Regions} regions, {Localities} localities.", countries, regions, localities);
        return new GazetteerImportResult(countries, regions, localities);
    }

    private async Task<int> ImportCountriesAsync(CancellationToken ct)
    {
        var existing = await db.AdminAreas.Where(a => a.Level == AdminLevel.Country && a.GeonamesId != null)
            .Select(a => a.GeonamesId!.Value).ToHashSetAsync(ct);
        var text = await Http.GetStringAsync($"{BaseUrl}/countryInfo.txt", ct);
        var added = 0;
        foreach (var line in Lines(text))
        {
            if (ParseCountry(line) is not { } c || existing.Contains(c.GeonamesId)) continue;
            db.AdminAreas.Add(new AdminArea { Id = Guid.NewGuid(), Level = AdminLevel.Country, Name = c.Name, IsoCode = c.Iso, GeonamesId = c.GeonamesId });
            existing.Add(c.GeonamesId);
            added++;
        }
        await db.SaveChangesAsync(ct);
        return added;
    }

    private async Task<int> ImportRegionsAsync(Dictionary<string, Guid> byIso, CancellationToken ct)
    {
        var existing = await db.AdminAreas.Where(a => a.Level == AdminLevel.Region && a.GeonamesId != null)
            .Select(a => a.GeonamesId!.Value).ToHashSetAsync(ct);
        var text = await Http.GetStringAsync($"{BaseUrl}/admin1CodesASCII.txt", ct);
        var added = 0;
        foreach (var line in Lines(text))
        {
            if (ParseAdmin1(line) is not { } r || existing.Contains(r.GeonamesId)) continue;
            var iso = r.Code.Split('.')[0];
            db.AdminAreas.Add(new AdminArea
            {
                Id = Guid.NewGuid(), Level = AdminLevel.Region, Name = r.Name, IsoCode = r.Code,
                WithinAreaId = byIso.TryGetValue(iso, out var cid) ? cid : null, GeonamesId = r.GeonamesId,
            });
            existing.Add(r.GeonamesId);
            added++;
        }
        await db.SaveChangesAsync(ct);
        return added;
    }

    private async Task<int> ImportLocalitiesAsync(Dictionary<string, Guid> byIso, Dictionary<string, Guid> byAdmin1, CancellationToken ct)
    {
        var existing = await db.AdminAreas.Where(a => a.Level == AdminLevel.Locality && a.GeonamesId != null)
            .Select(a => a.GeonamesId!.Value).ToHashSetAsync(ct);

        await using var zip = await Http.GetStreamAsync($"{BaseUrl}/cities500.zip", ct);
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read);
        var entry = archive.GetEntry("cities500.txt") ?? throw new InvalidOperationException("cities500.txt missing from archive.");
        await using var stream = entry.Open();
        using var reader = new StreamReader(stream);

        var added = 0;
        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null)
        {
            if (ParseCity(line) is not { } city || existing.Contains(city.GeonamesId)) continue;
            var parentId = byAdmin1.TryGetValue($"{city.CountryCode}.{city.Admin1}", out var rid) ? rid
                : byIso.TryGetValue(city.CountryCode, out var cid) ? cid : (Guid?)null;
            db.AdminAreas.Add(new AdminArea
            {
                Id = Guid.NewGuid(), Level = AdminLevel.Locality, Name = city.Name, WithinAreaId = parentId,
                Centroid = new Point(city.Lon, city.Lat) { SRID = 4326 }, GeonamesId = city.GeonamesId,
            });
            existing.Add(city.GeonamesId);
            if (++added % 5000 == 0) await db.SaveChangesAsync(ct);
        }
        await db.SaveChangesAsync(ct);
        return added;
    }

    // ---- parsing (pure, testable) ----

    internal readonly record struct CountryRow(string Iso, string Name, long GeonamesId);
    internal readonly record struct Admin1Row(string Code, string Name, long GeonamesId);
    internal readonly record struct CityRow(long GeonamesId, string Name, double Lat, double Lon, string CountryCode, string Admin1);

    internal static CountryRow? ParseCountry(string line)
    {
        if (line.Length == 0 || line[0] == '#') return null;
        var f = line.Split('\t');
        if (f.Length < 17 || f[0].Length == 0 || !long.TryParse(f[16], out var gid)) return null;
        return new CountryRow(f[0], f[4], gid);
    }

    internal static Admin1Row? ParseAdmin1(string line)
    {
        if (line.Length == 0) return null;
        var f = line.Split('\t');
        if (f.Length < 4 || !f[0].Contains('.') || !long.TryParse(f[3], out var gid)) return null;
        return new Admin1Row(f[0], f[1], gid);
    }

    internal static CityRow? ParseCity(string line)
    {
        if (line.Length == 0) return null;
        var f = line.Split('\t');
        if (f.Length < 15 || !long.TryParse(f[0], out var gid)) return null;
        if (!double.TryParse(f[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)) return null;
        if (!double.TryParse(f[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon)) return null;
        return new CityRow(gid, f[1], lat, lon, f[8], f[10]);
    }

    private static IEnumerable<string> Lines(string text) =>
        text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0);
}
