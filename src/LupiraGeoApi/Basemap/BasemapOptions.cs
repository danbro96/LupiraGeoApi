namespace LupiraGeoApi.Basemap;

/// <summary>Self-hosted MapLibre basemap assets (style + pmtiles + glyphs/sprites), served under <c>/basemap</c>.
/// <see cref="AssetsPath"/> unset ⇒ the whole surface 404s (dev without assets). Expected layout under the path:
/// <c>tiles/basemap.pmtiles</c>, <c>fonts/&lt;fontstack&gt;/&lt;range&gt;.pbf</c>, <c>sprites/v4/…</c>.</summary>
public sealed class BasemapOptions
{
    public const string SectionName = "Basemap";

    /// <summary>Directory holding the basemap assets (a read-only volume mount in prod).</summary>
    public string? AssetsPath { get; set; }

    /// <summary>Prefix substituted for <c>{BASE}</c> in style.json — the path clients reach <c>/basemap</c> under.
    /// Root-relative by default (resolved against the consuming page's origin behind the CalWeb BFF proxy); set an
    /// absolute URL for clients that need one.</summary>
    public string PublicBasePath { get; set; } = "/geo-api/basemap";
}
