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
