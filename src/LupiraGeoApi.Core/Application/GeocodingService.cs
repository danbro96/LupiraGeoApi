using System.Globalization;
using System.Text.Json;
using LupiraGeoApi.Domain;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LupiraGeoApi.Application;

/// <summary>A geocoding hit: a coordinate + display label + best-effort structured address and category.</summary>
public sealed record GeocodeHit(
    string DisplayName, double Lat, double Lon, PlaceCategory Category,
    string? CountryCode, string? Country, string? Region, string? Locality,
    string? OsmType, long? OsmId);

/// <summary>Forward + reverse geocoding against a self-hosted Nominatim, resolve-once-and-freeze into a
/// <see cref="GeocodeCache"/> keyed by a deterministic id (quantized grid for reverse, normalized query for forward).
/// If <c>Nominatim:BaseUrl</c> is unset (or on any failure) it returns cache-only / empty — it never blocks a resolve
/// and never calls an external service. Modelled on LupiraLocationApi's PlaceLabelService.</summary>
public sealed class GeocodingService(IDocumentSession session, IConfiguration config, ILogger<GeocodingService> logger)
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    private string? BaseUrl => config["Nominatim:BaseUrl"] is { Length: > 0 } b ? b.TrimEnd('/') : null;

    public async Task<GeocodeHit?> ReverseAsync(double lat, double lon, CancellationToken ct = default)
    {
        var id = GeocodeCache.ReverseId(lat, lon);
        if (await session.LoadAsync<GeocodeCache>(id, ct) is { } cached)
        {
            using var cdoc = JsonDocument.Parse(cached.Payload);
            return ParseHit(cdoc.RootElement);
        }

        if (BaseUrl is null) return null;
        var (qlat, qlon) = GeocodeCache.Quantize(lat, lon);
        var url = $"{BaseUrl}/reverse?format=jsonv2&addressdetails=1&lat={Fmt(qlat)}&lon={Fmt(qlon)}";
        using var doc = await GetAsync(url, ct);
        if (doc is null) return null;
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("lat", out _)) return null;

        await CacheAsync(id, "reverse", $"{qlat},{qlon}", root, ct);
        return ParseHit(root);
    }

    public async Task<IReadOnlyList<GeocodeHit>> ForwardAsync(string query, int limit = 5, CancellationToken ct = default)
    {
        query = query.Trim();
        if (query.Length == 0) return [];

        var id = GeocodeCache.ForwardId(query);
        if (await session.LoadAsync<GeocodeCache>(id, ct) is { } cached)
        {
            using var cdoc = JsonDocument.Parse(cached.Payload);
            return ParseArray(cdoc.RootElement);
        }

        if (BaseUrl is null) return [];
        var url = $"{BaseUrl}/search?format=jsonv2&addressdetails=1&limit={limit}&q={Uri.EscapeDataString(query)}";
        using var doc = await GetAsync(url, ct);
        if (doc is null) return [];
        var root = doc.RootElement;

        await CacheAsync(id, "forward", query, root, ct);
        return ParseArray(root);
    }

    private async Task<JsonDocument?> GetAsync(string url, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.UserAgent.ParseAdd("LupiraGeoApi/1.0");
            using var resp = await Http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return null;
            return JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Geocode request failed ({Url}); returning cache-only.", url);
            return null;
        }
    }

    private async Task CacheAsync(Guid id, string kind, string key, JsonElement payload, CancellationToken ct)
    {
        session.Store(new GeocodeCache { Id = id, Kind = kind, Key = key, Payload = payload.GetRawText(), ResolvedAt = DateTimeOffset.UtcNow });
        await session.SaveChangesAsync(ct);
    }

    private static IReadOnlyList<GeocodeHit> ParseArray(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array) return [];
        var hits = new List<GeocodeHit>();
        foreach (var el in root.EnumerateArray())
            if (ParseHit(el) is { } hit) hits.Add(hit);
        return hits;
    }

    private static GeocodeHit? ParseHit(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        if (!TryDouble(el, "lat", out var lat) || !TryDouble(el, "lon", out var lon)) return null;
        var display = Str(el, "display_name") ?? Str(el, "name") ?? "";

        string? cc = null, country = null, region = null, locality = null;
        if (el.TryGetProperty("address", out var a) && a.ValueKind == JsonValueKind.Object)
        {
            cc = Str(a, "country_code")?.ToUpperInvariant();
            country = Str(a, "country");
            region = Str(a, "state") ?? Str(a, "region") ?? Str(a, "province");
            locality = Str(a, "city") ?? Str(a, "town") ?? Str(a, "village") ?? Str(a, "municipality");
        }

        var category = MapCategory(Str(el, "type"), Str(el, "category") ?? Str(el, "class"));
        var osmType = Str(el, "osm_type");
        long? osmId = el.TryGetProperty("osm_id", out var o) && o.TryGetInt64(out var v) ? v : null;
        return new GeocodeHit(display, lat, lon, category, cc, country, region, locality, osmType, osmId);
    }

    /// <summary>Best-effort Nominatim OSM type/class → coarse <see cref="PlaceCategory"/>.</summary>
    private static PlaceCategory MapCategory(string? type, string? klass) => type switch
    {
        "restaurant" or "fast_food" => PlaceCategory.Restaurant,
        "cafe" => PlaceCategory.Cafe,
        "bar" or "pub" or "nightclub" => PlaceCategory.Bar,
        "supermarket" or "convenience" or "grocery" => PlaceCategory.Grocery,
        "school" or "kindergarten" or "college" => PlaceCategory.School,
        "university" => PlaceCategory.University,
        "hospital" => PlaceCategory.Hospital,
        "clinic" or "doctors" or "dentist" => PlaceCategory.Clinic,
        "pharmacy" => PlaceCategory.Pharmacy,
        "gym" or "fitness_centre" or "sports_centre" => PlaceCategory.Gym,
        "park" or "garden" => PlaceCategory.Park,
        "aerodrome" or "airport" => PlaceCategory.Airport,
        "station" or "halt" or "subway_entrance" => PlaceCategory.Station,
        "bus_stop" => PlaceCategory.BusStop,
        "hotel" or "hostel" or "guest_house" => PlaceCategory.Hotel,
        "hotel " => PlaceCategory.Hotel,
        "attraction" or "monument" or "memorial" or "artwork" => PlaceCategory.Landmark,
        "townhall" or "government" => PlaceCategory.Government,
        "place_of_worship" => PlaceCategory.Worship,
        _ => klass switch
        {
            "shop" => PlaceCategory.Store,
            "tourism" => PlaceCategory.Landmark,
            "office" => PlaceCategory.Office,
            _ => PlaceCategory.Unknown,
        },
    };

    private static string? Str(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool TryDouble(JsonElement el, string name, out double value)
    {
        value = 0;
        if (!el.TryGetProperty(name, out var v)) return false;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.TryGetDouble(out value),
            JsonValueKind.String => double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value),
            _ => false,
        };
    }

    private static string Fmt(double d) => d.ToString(CultureInfo.InvariantCulture);
}
