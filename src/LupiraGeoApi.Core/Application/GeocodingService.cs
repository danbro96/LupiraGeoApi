using System.Globalization;
using System.Text.Json;
using LupiraGeoApi.Domain;
using Marten;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LupiraGeoApi.Application;

/// <summary>A geocoding hit: a coordinate + display label + best-effort structured address and category.</summary>
public sealed record GeocodeHit(
    string DisplayName, double Lat, double Lon, PlaceCategory Category,
    string? CountryCode, string? Country, string? Region, string? Locality,
    string? OsmType, long? OsmId);

/// <summary>Forward + reverse geocoding, resolve-once-and-freeze into a <see cref="GeocodeCache"/> keyed by a
/// deterministic id (quantized grid for reverse, normalized query for forward). Tries the self-hosted regional
/// Nominatim first; when it is unset or yields nothing, the public fallback (throttled via
/// <see cref="NominatimRateGate"/>) gets one shot — whichever answers is frozen, so a foreign query costs one
/// external call ever. Both unset (or any failure) ⇒ cache-only / empty; it never blocks a resolve.</summary>
public sealed class GeocodingService(
    IDocumentSession session, IHttpClientFactory httpFactory,
    IOptions<NominatimOptions> options, ILogger<GeocodingService> logger)
{
    public const string PrimaryClientName = "nominatim";
    public const string FallbackClientName = "nominatim-fallback";

    private string? PrimaryUrl => Normalize(options.Value.BaseUrl);
    private string? FallbackUrl => Normalize(options.Value.FallbackBaseUrl);

    // An empty env var binds over the option's default — fall back to it rather than sending a blank UA.
    private string UserAgent => string.IsNullOrWhiteSpace(options.Value.UserAgent)
        ? new NominatimOptions().UserAgent
        : options.Value.UserAgent;

    private static string? Normalize(string? url) => url is { Length: > 0 } ? url.TrimEnd('/') : null;

    public async Task<GeocodeHit?> ReverseAsync(double lat, double lon, CancellationToken ct = default)
    {
        var id = GeocodeCache.ReverseId(lat, lon);
        if (await session.LoadAsync<GeocodeCache>(id, ct) is { } cached)
        {
            using var cdoc = JsonDocument.Parse(cached.Payload);
            return ParseHit(cdoc.RootElement);
        }

        var (qlat, qlon) = GeocodeCache.Quantize(lat, lon);
        var pathQuery = $"/reverse?format=jsonv2&addressdetails=1&lat={Fmt(qlat)}&lon={Fmt(qlon)}";
        foreach (var (client, baseUrl) in Endpoints())
        {
            using var doc = await GetAsync(client, baseUrl + pathQuery, ct);
            // A regional instance answers "Unable to geocode" (no lat) for out-of-coverage points — try the fallback.
            if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Object
                            || !doc.RootElement.TryGetProperty("lat", out _)) continue;

            await CacheAsync(id, "reverse", $"{qlat},{qlon}", doc.RootElement, ct);
            return ParseHit(doc.RootElement);
        }
        return null;
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

        var pathQuery = $"/search?format=jsonv2&addressdetails=1&limit={limit}&q={Uri.EscapeDataString(query)}";
        JsonDocument? emptyResult = null;
        try
        {
            foreach (var (client, baseUrl) in Endpoints())
            {
                var doc = await GetAsync(client, baseUrl + pathQuery, ct);
                if (doc is null) continue;
                var hits = ParseArray(doc.RootElement);
                if (hits.Count > 0)
                {
                    await CacheAsync(id, "forward", query, doc.RootElement, ct);
                    doc.Dispose();
                    return hits;
                }
                emptyResult?.Dispose();
                emptyResult = doc; // valid empty answer — freeze it only if no later endpoint does better
            }

            if (emptyResult is not null)
                await CacheAsync(id, "forward", query, emptyResult.RootElement, ct);
            return [];
        }
        finally
        {
            emptyResult?.Dispose();
        }
    }

    private IEnumerable<(string Client, string BaseUrl)> Endpoints()
    {
        if (PrimaryUrl is { } p) yield return (PrimaryClientName, p);
        if (FallbackUrl is { } f) yield return (FallbackClientName, f);
    }

    private async Task<JsonDocument?> GetAsync(string clientName, string url, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.UserAgent.ParseAdd(UserAgent);
            using var resp = await httpFactory.CreateClient(clientName).SendAsync(req, ct);
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
