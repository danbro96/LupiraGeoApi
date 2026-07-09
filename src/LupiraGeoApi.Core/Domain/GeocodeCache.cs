using System.Globalization;

namespace LupiraGeoApi.Domain;

/// <summary>
/// A resolve-once-and-freeze geocoding cache (Marten document, <c>geo_user</c> schema), keyed by a deterministic id so
/// retries upsert. Reverse entries are keyed by a ~100 m quantized grid cell (nearby fixes share one entry); forward
/// entries by the normalized query. Keeps Nominatim calls down and lets the gazetteer stay usable when geocoding is
/// disabled. <see cref="Payload"/> is the raw upstream JSON.
/// </summary>
public sealed class GeocodeCache
{
    public Guid Id { get; set; }
    public string Kind { get; set; } = "";
    public string Key { get; set; } = "";
    public string Payload { get; set; } = "";
    public DateTimeOffset ResolvedAt { get; set; }

    /// <summary>~100 m grid quantization (≈0.001° lat), so one cell shares one reverse entry.</summary>
    public static (double Lat, double Lon) Quantize(double lat, double lon) => (Math.Round(lat, 3), Math.Round(lon, 3));

    public static Guid ReverseId(double lat, double lon)
    {
        var (qlat, qlon) = Quantize(lat, lon);
        return DeterministicGuid.From($"rev:{qlat.ToString(CultureInfo.InvariantCulture)}:{qlon.ToString(CultureInfo.InvariantCulture)}");
    }

    public static Guid ForwardId(string query) =>
        DeterministicGuid.From($"fwd:{query.Trim().ToLowerInvariant()}");
}
