using System.Globalization;
using Lupira.Primitives;

namespace LupiraGeoApi.Core.Domain;

/// <summary>
/// A resolve-once-and-freeze geocoding cache (Marten document, <c>geo_user</c> schema), keyed by a deterministic id so
/// retries upsert. Reverse entries are keyed by a ~100 m quantized grid cell (nearby fixes share one entry); forward
/// entries by the normalized query. Keeps Nominatim calls down and lets the gazetteer stay usable when geocoding is
/// disabled. <see cref="Payload"/> is the raw upstream JSON.
/// </summary>
public sealed class GeocodeCache
{
    public Guid Id { get; set; }

    public string Kind { get; set; } = string.Empty;

    public string Key { get; set; } = string.Empty;

    public string Payload { get; set; } = string.Empty;

    public DateTimeOffset ResolvedAt { get; set; }

    /// <summary>~100 m grid quantization (≈0.001° lat), so one cell shares one reverse entry.</summary>
    public static (double Lat, double Lon) Quantize(double lat, double lon) => (Math.Round(lat, 3), Math.Round(lon, 3));

    public static Guid ReverseId(double lat, double lon)
    {
        var (qlat, qlon) = Quantize(lat, lon);
        return DeterministicGuid.From($"rev:{qlat.ToString(CultureInfo.InvariantCulture)}:{qlon.ToString(CultureInfo.InvariantCulture)}");
    }

    /// <summary>~11 m grid for building-precision reverse.</summary>
    public static (double Lat, double Lon) QuantizeFine(double lat, double lon) => (Math.Round(lat, 4), Math.Round(lon, 4));

    public static Guid FineReverseId(double lat, double lon)
    {
        var (qlat, qlon) = QuantizeFine(lat, lon);
        return DeterministicGuid.From($"rev4:{qlat.ToString(CultureInfo.InvariantCulture)}:{qlon.ToString(CultureInfo.InvariantCulture)}");
    }

    public static Guid ForwardId(string query) =>
        DeterministicGuid.From($"fwd:{query.Trim().ToLowerInvariant()}");

    public static Guid StructuredForwardId(string? street, string? postcode, string? locality, string? countryCode) =>
        DeterministicGuid.From($"fwds:{StructuredKey(street, postcode, locality, countryCode)}");

    public static string StructuredKey(string? street, string? postcode, string? locality, string? countryCode) =>
        string.Join('|', Part(street), string.Concat((postcode ?? string.Empty).Where(c => !char.IsWhiteSpace(c))).ToLowerInvariant(),
            Part(locality), Part(countryCode));

    /// <summary>An OSM object lookup, keyed like <c>osm:W123</c>.</summary>
    public static Guid LookupId(string osmKey) => DeterministicGuid.From($"osm:{osmKey}");

    private static string Part(string? s) => (s ?? string.Empty).Trim().ToLowerInvariant();
}
