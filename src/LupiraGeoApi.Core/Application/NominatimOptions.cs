namespace LupiraGeoApi.Application;

/// <summary>The <c>Nominatim</c> config section. <see cref="BaseUrl"/> is the self-hosted regional instance,
/// <see cref="FallbackBaseUrl"/> an optional public endpoint tried when the primary is unset or yields nothing —
/// fallback traffic is throttled to the public usage policy (≤1 req/s, identifying User-Agent). Both unset ⇒
/// geocoding is cache-only.</summary>
public sealed class NominatimOptions
{
    public string? BaseUrl { get; set; }
    public string? FallbackBaseUrl { get; set; }
    public string UserAgent { get; set; } = "LupiraGeoApi/1.0 (+https://github.com/danbro96/LupiraGeoApi)";
}
