using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using LupiraGeoApi.Core.Application.Geocoding;
using Microsoft.Extensions.Options;

namespace LupiraGeoApi.Clients;

/// <summary><see cref="IAddressAutocomplete"/> over Photon's <c>/api</c> (free text) and <c>/structured</c> endpoints,
/// mapping its GeoJSON features onto <see cref="GeocodeHit"/> in the canonical geocoder's vocabulary.</summary>
public sealed partial class PhotonAddressAutocomplete(
    HttpClient http, IOptions<PhotonOptions> options, ILogger<PhotonAddressAutocomplete> logger) : IAddressAutocomplete
{
    private static readonly ActivitySource Source = new("LupiraGeoApi.AddressAutocomplete");

    public bool IsConfigured => BaseUrl is not null;

    private string? BaseUrl => options.Value.BaseUrl is { Length: > 0 } u ? u.TrimEnd('/') : null;

    public async Task<ForwardResult> SuggestAsync(AddressQuery query, GeoPoint? near, int limit, CancellationToken ct = default)
    {
        if (BaseUrl is not { } baseUrl) return ForwardResult.Empty;
        using var activity = Source.StartActivity("AddressAutocomplete.Suggest");
        activity?.SetTag("geo.autocomplete.structured", query.IsStructured);
        try
        {
            using var resp = await http.GetAsync(baseUrl + PathAndQuery(query, near, limit, options.Value.Language), ct);
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogWarning("Address autocomplete returned {Status}.", (int) resp.StatusCode);
                activity?.SetStatus(ActivityStatusCode.Error);
                return ForwardResult.Unavailable;
            }

            var hits = ParseFeatures(await resp.Content.ReadAsStringAsync(ct));
            activity?.SetTag("geo.autocomplete.hits", hits.Count);
            return hits.Count > 0 ? ForwardResult.FromHits(hits) : ForwardResult.Empty;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "Address autocomplete request failed.");
            activity?.SetStatus(ActivityStatusCode.Error);
            return ForwardResult.Unavailable;
        }
    }

    internal static string PathAndQuery(AddressQuery query, GeoPoint? near, int limit, string? language)
    {
        var args = new List<string>();
        if (query.Text is { } text)
        {
            args.Add("q=" + Uri.EscapeDataString(text));
        }
        else
        {
            if (query.Street is { } street)
            {
                var (name, number) = SplitStreet(street);
                args.Add("street=" + Uri.EscapeDataString(name));
                if (number is not null) args.Add("housenumber=" + Uri.EscapeDataString(number));
            }

            if (query.Postcode is { } postcode) args.Add("postcode=" + Uri.EscapeDataString(postcode));
            if (query.Locality is { } locality) args.Add("city=" + Uri.EscapeDataString(locality));
            if (query.CountryCode is { } cc) args.Add("countrycode=" + cc);
        }

        if (near is { } p) args.Add(string.Create(CultureInfo.InvariantCulture, $"lat={p.Lat}&lon={p.Lon}"));
        args.Add(string.Create(CultureInfo.InvariantCulture, $"limit={limit}"));
        if (language is { Length: > 0 }) args.Add("lang=" + Uri.EscapeDataString(language));
        return (query.IsStructured ? "/structured?" : "/api?") + string.Join('&', args);
    }

    internal static (string Street, string? HouseNumber) SplitStreet(string street) =>
        TrailingNumber().Match(street.Trim()) is { Success: true } m
            ? (m.Groups["street"].Value, m.Groups["number"].Value.Replace(" ", string.Empty, StringComparison.Ordinal))
            : (street.Trim(), null);

    internal static IReadOnlyList<GeocodeHit> ParseFeatures(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("features", out var features) || features.ValueKind != JsonValueKind.Array) return [];
        var hits = new List<GeocodeHit>();
        foreach (var feature in features.EnumerateArray())
            if (ParseFeature(feature) is { } hit) hits.Add(hit);
        return hits;
    }

    private static GeocodeHit? ParseFeature(JsonElement feature)
    {
        if (!feature.TryGetProperty("geometry", out var geometry)
            || !geometry.TryGetProperty("coordinates", out var coords) || coords.GetArrayLength() < 2
            || !feature.TryGetProperty("properties", out var p) || p.ValueKind != JsonValueKind.Object)
            return null;

        var lon = coords[0].GetDouble();
        var lat = coords[1].GetDouble();
        var layer = Str(p, "type");
        var key = Str(p, "osm_key");
        var value = Str(p, "osm_value");
        var name = Str(p, "name");
        var street = Str(p, "street") ?? (layer == "street" ? name : null);
        var houseNumber = Str(p, "housenumber");
        var postcode = Str(p, "postcode");
        var locality = Str(p, "city") ?? (layer == "city" ? name : null);

        // Photon's layer stands in for Nominatim's addresstype; its osm_key is "place" even for plain address points.
        var addressType = layer switch
        {
            "city" or "county" or "state" or "country" => layer,
            "street" => "road",
            _ => null,
        };
        var isArea = AreaClassifier.IsArea(addressType, key, value);
        long? osmId = p.TryGetProperty("osm_id", out var o) && o.TryGetInt64(out var v) ? v : null;
        var display = string.Join(", ", new[]
        {
            name is not null && name != street ? name : null,
            street is null ? null : houseNumber is null ? street : $"{street} {houseNumber}",
            string.Join(' ', new[] { postcode, locality is not null && locality != name ? locality : null }.OfType<string>()),
            Str(p, "country"),
        }.Where(s => !string.IsNullOrEmpty(s)));

        return new GeocodeHit(
            DisplayName: display,
            Lat: lat,
            Lon: lon,
            Category: OsmCategory.Map(value, key),
            CountryCode: Str(p, "countrycode")?.ToUpperInvariant(),
            Country: Str(p, "country"),
            Region: Str(p, "state"),
            Locality: locality,
            OsmType: OsmIdentity.TypeName(Str(p, "osm_type")),
            OsmId: osmId,
            IsArea: isArea,
            Postcode: postcode,
            HouseNumber: houseNumber,
            IsCoarse: isArea || layer is not (null or "house" or "other"),
            Street: street,
            Name: name);
    }

    private static string? Str(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;

    [GeneratedRegex(@"^(?<street>.*\p{L}.*?)\s+(?<number>\d{1,4}(?:\s?\p{L})?)$")]
    private static partial Regex TrailingNumber();
}
