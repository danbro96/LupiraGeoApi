using System.Globalization;
using System.Net;
using System.Text.Json;
using LupiraGeoApi.Core.Domain;
using Marten;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LupiraGeoApi.Core.Application.Geocoding;

/// <summary>Forward + reverse geocoding, resolve-once-and-freeze into a <see cref="GeocodeCache"/> keyed by a
/// deterministic id (quantized grid for reverse, normalized query for forward). Tries the self-hosted regional
/// Nominatim first; when it is unset or yields nothing usable (a forward search with no hit passing
/// <see cref="ForwardHitFilter"/>, or a reverse hit no finer than a country — its worldwide country_osm_grid
/// fallback), the public fallback (throttled via
/// <see cref="NominatimRateGate"/>) gets one shot — whichever answers is frozen, so a foreign query costs one
/// external call ever. Both unset (or any failure) ⇒ cache-only / empty; it never blocks a resolve.</summary>
public sealed class GeocodingService(
    IDocumentSession session, IHttpClientFactory httpFactory,
    IOptions<NominatimOptions> options, ILogger<GeocodingService> logger)
{
    public const string PrimaryClientName = "nominatim";
    public const string FallbackClientName = "nominatim-fallback";

    private static readonly TimeSpan EmptyAnswerTtl = TimeSpan.FromDays(1);

    // More than asked: the right hit can rank below a rejected one (Katrineholm's Valla is 4th for "640 23 Valla").
    private const int CandidatePool = 10;

    private string? PrimaryUrl => Normalize(options.Value.BaseUrl);

    private string? FallbackUrl => Normalize(options.Value.FallbackBaseUrl);

    // An empty env var binds over the option's default — fall back to it rather than sending a blank UA.
    private string UserAgent => string.IsNullOrWhiteSpace(options.Value.UserAgent)
        ? new NominatimOptions().UserAgent
        : options.Value.UserAgent;

    private static string? Normalize(string? url) => url is { Length: > 0 } ? url.TrimEnd('/') : null;

    public async Task<GeocodeHit?> ReverseAsync(
        double lat, double lon, ReversePrecision precision = ReversePrecision.Area, CancellationToken ct = default)
    {
        var fine = precision == ReversePrecision.Building;
        var id = fine ? GeocodeCache.FineReverseId(lat, lon) : GeocodeCache.ReverseId(lat, lon);
        if (await session.LoadAsync<GeocodeCache>(id, ct) is { } cached)
        {
            using var cdoc = JsonDocument.Parse(cached.Payload);
            return ParseHit(cdoc.RootElement);
        }

        var (qlat, qlon) = fine ? GeocodeCache.QuantizeFine(lat, lon) : GeocodeCache.Quantize(lat, lon);
        var pathQuery = $"/reverse?format=jsonv2&addressdetails=1&lat={Fmt(qlat)}&lon={Fmt(qlon)}{(fine ? "&zoom=18" : string.Empty)}";
        JsonDocument? coarse = null;
        try
        {
            foreach (var (client, baseUrl) in Endpoints())
            {
                var doc = await GetAsync(client, baseUrl + pathQuery, ct);
                // No usable object (a truly out-of-coverage instance can answer "Unable to geocode", no lat).
                if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Object
                                || !doc.RootElement.TryGetProperty("lat", out _))
                                {
                                    doc?.Dispose();
                    continue;
                                }

                // The regional instance country-matches every out-of-coverage point via Nominatim's worldwide
                // country_osm_grid — a bare country centroid, not a real fix. Keep it only as a last resort and let
                // the public fallback answer with street detail; freeze the coarse hit only if nothing better comes.
                if (IsCountryLevel(doc.RootElement))
                {
                    coarse?.Dispose();
                    coarse = doc;
                    continue;
                }

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

    public Task<ForwardResult> ForwardAsync(
        string query, int limit = 5, ForwardCacheMode cacheMode = ForwardCacheMode.Default, CancellationToken ct = default) =>
        string.IsNullOrWhiteSpace(query)
            ? Task.FromResult(ForwardResult.Empty)
            : ForwardAsync(AddressQuery.FromText(query), limit, cacheMode, ct);

    /// <summary>An <see cref="GeocodeStatus.Unavailable"/> outcome writes nothing, so an outage can't destroy a
    /// previously frozen answer in any <paramref name="cacheMode"/>.</summary>
    public async Task<ForwardResult> ForwardAsync(
        AddressQuery query, int limit = 5, ForwardCacheMode cacheMode = ForwardCacheMode.Default, CancellationToken ct = default)
    {
        var regional = RegionalCountries();
        List<GeocodeHit> Accepted(JsonElement root) =>
            ParseArray(root).Where(h => ForwardHitFilter.Accepts(query, h, regional)).Take(limit).ToList();

        var id = query.CacheId;
        if (cacheMode != ForwardCacheMode.Bypass && await session.LoadAsync<GeocodeCache>(id, ct) is { } cached)
        {
            using var cdoc = JsonDocument.Parse(cached.Payload);
            var cachedHits = Accepted(cdoc.RootElement);
            // Hits stay frozen forever — those don't move. An empty (or all-rejected) answer only holds briefly: OSM
            // gets the missing address eventually, and a stale empty would keep a stub unhealable.
            if (cachedHits.Count > 0
                || (cacheMode == ForwardCacheMode.Default && DateTimeOffset.UtcNow - cached.ResolvedAt < EmptyAnswerTtl))
                return ForwardResult.FromHits(cachedHits);
        }

        var pathQuery = SearchPath(query, Math.Max(limit, CandidatePool));
        JsonDocument? emptyResult = null;
        var anyFailure = false;
        try
        {
            foreach (var (client, baseUrl) in Endpoints())
            {
                var fetch = await GetAsync(client, baseUrl + pathQuery, ct);
                if (fetch is null)
                {
                    anyFailure = true;
                    continue;
                } // transport failure after retries

                var hits = Accepted(fetch.RootElement);
                if (hits.Count > 0)
                {
                    await CacheAsync(id, "forward", query.CacheKey, fetch.RootElement, ct);
                    fetch.Dispose();
                    return ForwardResult.FromHits(hits);
                }

                emptyResult?.Dispose();
                emptyResult = fetch; // empty or all-rejected — freeze it only if no later endpoint does better
            }

            // A definitive empty answer from any endpoint wins; only refuse (Unavailable) when nothing answered at
            // all, so a transient outage never gets frozen as a no-hit or turned into a coordinate-less stub.
            if (emptyResult is not null)
            {
                await CacheAsync(id, "forward", query.CacheKey, emptyResult.RootElement, ct);
                return ForwardResult.Empty;
            }

            return anyFailure ? ForwardResult.Unavailable : ForwardResult.Empty;
        }
        finally
        {
            emptyResult?.Dispose();
        }
    }

    /// <summary>The regional instance alone, uncached and without the bare-word bar: typeahead input is partial by
    /// nature, and the public endpoint's usage policy forbids autocomplete traffic.</summary>
    public async Task<ForwardResult> ForwardRegionalAsync(AddressQuery query, int limit, CancellationToken ct = default)
    {
        if (PrimaryUrl is not { } baseUrl) return ForwardResult.Empty;
        using var doc = await GetAsync(PrimaryClientName, baseUrl + SearchPath(query, limit), ct);
        if (doc is null) return ForwardResult.Unavailable;
        var hits = ParseArray(doc.RootElement).Where(h => ForwardHitFilter.MatchesPostcode(query, h)).Take(limit).ToList();
        return hits.Count > 0 ? ForwardResult.FromHits(hits) : ForwardResult.Empty;
    }

    /// <summary>Hits frozen for a text query, without asking any endpoint.</summary>
    public async Task<IReadOnlyList<GeocodeHit>> FrozenForwardHitsAsync(string query, CancellationToken ct = default) =>
        string.IsNullOrWhiteSpace(query) || await session.LoadAsync<GeocodeCache>(GeocodeCache.ForwardId(query), ct) is not { } cached
            ? []
            : ParseCached(cached.Payload);

    /// <summary>One OSM object by identity (<c>/lookup</c>), regional first, then the public fallback. Hits are frozen
    /// under <c>osm:W123</c>; a miss is not, so an object OSM gains later is found on retry.</summary>
    public async Task<ForwardResult> LookupAsync(char osmLetter, long osmId, CancellationToken ct = default)
    {
        var osmKey = $"{osmLetter}{osmId.ToString(CultureInfo.InvariantCulture)}";
        var id = GeocodeCache.LookupId(osmKey);
        if (await session.LoadAsync<GeocodeCache>(id, ct) is { } cached && ParseCached(cached.Payload) is { Count: > 0 } frozen)
            return ForwardResult.FromHits(frozen);

        var anyAnswer = false;
        foreach (var (client, baseUrl) in Endpoints())
        {
            using var doc = await GetAsync(client, $"{baseUrl}/lookup?format=jsonv2&addressdetails=1&osm_ids={osmKey}", ct);
            if (doc is null) continue;
            anyAnswer = true;
            var hits = ParseArray(doc.RootElement).Where(h => h.OsmId == osmId).ToList();
            if (hits.Count == 0) continue;
            await CacheAsync(id, "lookup", $"osm:{osmKey}", doc.RootElement, ct);
            return ForwardResult.FromHits(hits);
        }

        return anyAnswer || !Endpoints().Any() ? ForwardResult.Empty : ForwardResult.Unavailable;
    }

    private static string SearchPath(AddressQuery query, int limit)
    {
        var path = $"/search?format=jsonv2&addressdetails=1&limit={limit}";
        if (query.Text is { } text) return path + "&q=" + Uri.EscapeDataString(text);
        if (query.Street is { } street) path += "&street=" + Uri.EscapeDataString(street);
        if (query.Locality is { } locality) path += "&city=" + Uri.EscapeDataString(locality);
        if (query.Postcode is { } postcode) path += "&postalcode=" + Uri.EscapeDataString(postcode);
        if (query.CountryCode is { } cc) path += "&countrycodes=" + cc.ToLowerInvariant();
        return path;
    }

    private HashSet<string> RegionalCountries() =>
        (options.Value.RegionalCountries ?? string.Empty)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

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

                logger.LogWarning("Geocode {Url} returned {Status}.", url, (int) resp.StatusCode);
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

    private static bool IsTransient(HttpStatusCode s) => s == HttpStatusCode.TooManyRequests || (int) s >= 500;

    private static TimeSpan Backoff(int attempt) => TimeSpan.FromMilliseconds(250 * (attempt + 1));

    private static TimeSpan? RetryAfter(HttpResponseMessage resp) =>
        resp.Headers.RetryAfter?.Delta is { } d ? (d <= TimeSpan.FromSeconds(5) ? d : TimeSpan.FromSeconds(5)) : null;

    private async Task CacheAsync(Guid id, string kind, string key, JsonElement payload, CancellationToken ct)
    {
        session.Store(new GeocodeCache { Id = id, Kind = kind, Key = key, Payload = payload.GetRawText(), ResolvedAt = DateTimeOffset.UtcNow });
        await session.SaveChangesAsync(ct);
    }

    /// <summary>Re-parse a frozen <see cref="GeocodeCache.Payload"/>: a forward array or a single reverse object.
    /// A malformed payload yields no hits.</summary>
    public static IReadOnlyList<GeocodeHit> ParseCached(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            return root.ValueKind == JsonValueKind.Array ? ParseArray(root) : ParseHit(root) is { } hit ? [hit] : [];
        }
        catch (JsonException)
        {
            return [];
        }
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
        var display = Str(el, "display_name") ?? Str(el, "name") ?? string.Empty;

        string? cc = null, country = null, region = null, locality = null, postcode = null, houseNumber = null, street = null;
        if (el.TryGetProperty("address", out var a) && a.ValueKind == JsonValueKind.Object)
        {
            cc = Str(a, "country_code")?.ToUpperInvariant();
            country = Str(a, "country");
            region = Str(a, "state") ?? Str(a, "region") ?? Str(a, "province");
            locality = Str(a, "city") ?? Str(a, "town") ?? Str(a, "village") ?? Str(a, "municipality");
            postcode = Str(a, "postcode");
            houseNumber = Str(a, "house_number");
            street = Str(a, "road") ?? Str(a, "pedestrian");
        }

        var type = Str(el, "type");
        var osmClass = Str(el, "category") ?? Str(el, "class");
        var category = OsmCategory.Map(type, osmClass);
        var osmType = Str(el, "osm_type");
        long? osmId = el.TryGetProperty("osm_id", out var o) && o.TryGetInt64(out var v) ? v : null;
        var addressType = Str(el, "addresstype");
        var isArea = AreaClassifier.IsArea(addressType, osmClass, type);
        var isCoarse = isArea || addressType == "road" || osmClass is "highway" or "place" or "boundary" or "landuse";
        double? importance = TryDouble(el, "importance", out var imp) ? imp : null;
        return new GeocodeHit(
            display, lat, lon, category, cc, country, region, locality, osmType, osmId, isArea, postcode, importance,
            houseNumber, isCoarse, street, Str(el, "name") is { Length: > 0 } name ? name : null);
    }

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
