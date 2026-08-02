using LupiraGeoApi.Application;
using Microsoft.Extensions.Options;

namespace LupiraGeoApi.Dependencies;

/// <summary>One outward edge. The geocoders are anonymous HTTP; the User-Agent is the only header
/// they care about (the public endpoint rejects requests without an identifying one).</summary>
public sealed class DependencyTarget
{
    public required string Name { get; set; }
    public required string BaseUrl { get; set; }
    public required string ProbePath { get; set; }
    public string? UserAgent { get; set; }
}

/// <summary>Roster derived from the same options the geocoder binds — edges cannot drift. Both
/// Nominatim URLs unset is a supported mode (cache-only geocoding), reported as Unconfigured.</summary>
public static class DependencyTargets
{
    public static IReadOnlyList<DependencyTarget> From(IOptions<NominatimOptions> nominatim, IConfiguration config)
    {
        var opts = nominatim.Value;
        return
        [
            new DependencyTarget
            {
                Name = "nominatim-api",
                BaseUrl = opts.BaseUrl ?? "",
                // A trivially cheap reverse lookup: /status is not exposed by every Nominatim build.
                ProbePath = "search?format=jsonv2&limit=1&q=a",
                UserAgent = opts.UserAgent,
            },
            new DependencyTarget
            {
                Name = "nominatim-public",
                BaseUrl = opts.FallbackBaseUrl ?? "",
                ProbePath = "search?format=jsonv2&limit=1&q=a",
                UserAgent = opts.UserAgent,
            },
            new DependencyTarget
            {
                Name = "geonames",
                BaseUrl = config["Geonames:BaseUrl"] is { Length: > 0 } b ? b : "https://download.geonames.org/export/dump",
                ProbePath = "readme.txt",
                UserAgent = opts.UserAgent,
            },
        ];
    }
}
