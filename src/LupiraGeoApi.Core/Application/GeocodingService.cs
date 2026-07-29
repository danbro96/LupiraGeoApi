using LupiraGeoApi.Domain;
using Marten;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Net;
using System.Text.Json;

namespace LupiraGeoApi.Application;

/// <summary>A geocoding hit: a coordinate + display label + best-effort structured address and category.</summary>
public sealed record GeocodeHit(
    string DisplayName, double Lat, double Lon, PlaceCategory Category,
    string? CountryCode, string? Country, string? Region, string? Locality,
    string? OsmType, long? OsmId);

/// <summary>Outcome of a forward geocode. <c>Ok</c> carries hits; <c>Empty</c> is a definitive "no such place"
/// (safe to freeze/provision); <c>Unavailable</c> means no endpoint could be reached (transport error/timeout/429/5xx
/// after retries) — transient, NOT a no-hit, so callers must not persist a coordinate-less stub for it.</summary>
public enum GeocodeStatus { Ok, Empty, Unavailable }

public sealed record ForwardResult(GeocodeStatus Status, IReadOnlyList<GeocodeHit> Hits)
{
    public static readonly ForwardResult Empty = new(GeocodeStatus.Empty, []);
    public static readonly ForwardResult Unavailable = new(GeocodeStatus.Unavailable, []);
    public static ForwardResult FromHits(IReadOnlyList<GeocodeHit> hits) => new(GeocodeStatus.Ok, hits);
}

/// <summary>Forward + reverse geocoding, resolve-once-and-freeze into a <see cref="GeocodeCache"/> keyed by a
/// deterministic id (quantized grid for reverse, normalized query for forward). Tries the self-hosted regional
/// Nominatim first; when it is unset or yields nothing usable (an empty forward search, or a reverse hit no finer
/// than a country — its worldwide country_osm_grid fallback), the public fallback (throttled via
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
        JsonDocument? coarse = null;
        try
        {
            foreach (var (client, baseUrl) in Endpoints())
            {
                var doc = await GetAsync(client, baseUrl + pathQuery, ct);
                // No usable object (a truly out-of-coverage instance can answer "Unable to geocode", no lat).
                if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Object
                                || !doc.RootElement.TryGetProperty("lat", out _)) { doc?.Dispose(); continue; }

                // The regional instance country-matches every out-of-coverage point via Nominatim's worldwide
                // country_osm_grid — a bare country centroid, not a real fix. Keep it only as a last resort and let
                // the public fallback answer with street detail; freeze the coarse hit only if nothing better comes.
                if (IsCountryLevel(doc.RootElement)) { coarse?.Dispose(); coarse = doc; continue; }

                var hit = ParseHit(doc.RootElement);
                await CacheAsync(id, "reverse", $"{qlat},{qlon}", doc.RootElement, ct);
                doc.Dispose();
                return hit;
            }

            if (coarse is not null)
            {
                var hit = ParseHit(coarse.RootElement);
                await CacheAsync(id, "reverse", $"{qlat},{qlon}", coarse.RootElement, ct);
                return hit;
            }
            return null;
        }
        finally
        {
            coarse?.Dispose();
        }
    }

    public async Task<ForwardResult> ForwardAsync(string query, int limit = 5, CancellationToken ct = default)
    {
        query = query.Trim();
        if (query.Length == 0) return ForwardResult.Empty;

        var id = GeocodeCache.ForwardId(query);
        if (await session.LoadAsync<GeocodeCache>(id, ct) is { } cached)
        {
            using var cdoc = JsonDocument.Parse(cached.Payload);
            return ForwardResult.FromHits(ParseArray(cdoc.RootElement));
        }

        var pathQuery = $"/search?format=jsonv2&addressdetails=1&limit={limit}&q={Uri.EscapeDataString(query)}";
        JsonDocument? emptyResult = null;
        var anyFailure = false;
        try
        {
            foreach (var (client, baseUrl) in Endpoints())
            {
                var fetch = await GetAsync(client, baseUrl + pathQuery, ct);
                if (fetch is null) { anyFailure = true; continue; } // transport failure after retries
                var hits = ParseArray(fetch.RootElement);
                if (hits.Count > 0)
                {
                    await CacheAsync(id, "forward", query, fetch.RootElement, ct);
                    fetch.Dispose();
                    return ForwardResult.FromHits(hits);
                }
                emptyResult?.Dispose();
                emptyResult = fetch; // valid empty answer — freeze it only if no later endpoint does better
            }

            // A definitive empty answer from any endpoint wins; only refuse (Unavailable) when nothing answered at
            // all, so a transient outage never gets frozen as a no-hit or turned into a coordinate-less stub.
            if (emptyResult is not null)
            {
                await CacheAsync(id, "forward", query, emptyResult.RootElement, ct);
                return ForwardResult.Empty;
            }
            return anyFailure ? ForwardResult.Unavailable : ForwardResult.Empty;
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

    // 429/5xx and transport errors are transient under load; a bare failure here would let the caller mint a
    // permanent coordinate-less stub. Retry a few times (honoring Retry-After on 429) before giving up — status codes
    // come back fast so they get one extra try over slow transport errors, which bounds primary-down latency.
    private const int MaxStatusRetries = 2;
    private const int MaxErrorRetries = 1;

    private async Task<JsonDocument?> GetAsync(string clientName, string url, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.UserAgent.ParseAdd(UserAgent);
                using var resp = await httpFactory.CreateClient(clientName).SendAsync(req, ct);
                if (resp.IsSuccessStatusCode)
                    return JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
                if (attempt < MaxStatusRetries && IsTransient(resp.StatusCode))
                {
                    await Task.Delay(RetryAfter(resp) ?? Backoff(attempt), ct);
                    continue;
                }
                logger.LogWarning("Geocode {Url} returned {Status}.", url, (int)resp.StatusCode);
                return null;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // the caller cancelled — not a geocoder failure
            }
            catch (Exception ex)
            {
                if (attempt < MaxErrorRetries)
                {
                    logger.LogDebug(ex, "Geocode {Url} attempt {Attempt} failed; retrying.", url, attempt + 1);
                    await Task.Delay(Backoff(attempt), ct);
                    continue;
                }
                logger.LogWarning(ex, "Geocode request failed ({Url}) after {Attempts} attempts.", url, attempt + 1);
                return null;
            }
        }
    }

    private static bool IsTransient(HttpStatusCode s) => s == HttpStatusCode.TooManyRequests || (int)s >= 500;

    private static TimeSpan Backoff(int attempt) => TimeSpan.FromMilliseconds(250 * (attempt + 1));

    private static TimeSpan? RetryAfter(HttpResponseMessage resp) =>
        resp.Headers.RetryAfter?.Delta is { } d ? (d <= TimeSpan.FromSeconds(5) ? d : TimeSpan.FromSeconds(5)) : null;

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

    private static readonly string[] SubCountryFields =
        ["road", "neighbourhood", "hamlet", "quarter", "suburb", "city_district", "city", "town", "village",
         "municipality", "county", "state_district", "state", "region", "province"];

    /// <summary>A reverse hit that resolves no finer than a country — the regional instance's
    /// <c>country_osm_grid</c> fallback for an out-of-coverage point (a country centroid, no locality). Such a hit is
    /// worth escalating to the public endpoint rather than freezing.</summary>
    private static bool IsCountryLevel(JsonElement el)
    {
        if (Str(el, "addresstype") == "country") return true;
        if (!el.TryGetProperty("address", out var a) || a.ValueKind != JsonValueKind.Object) return true;
        foreach (var f in SubCountryFields)
            if (a.TryGetProperty(f, out _)) return false;
        return true;
    }

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
