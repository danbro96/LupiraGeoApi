using LupiraGeoApi.Basemap;
using Microsoft.Extensions.Options;

namespace LupiraGeoApi.Endpoints;

/// <summary>Self-hosted MapLibre basemap: a themed style.json plus static assets (pmtiles with HTTP range, glyphs,
/// sprites). Excluded from OpenAPI — MapLibre consumes these by convention, not via generated clients.</summary>
public static class BasemapEndpoints
{
    private static readonly string[] Themes = ["light", "dark"];

    public static IEndpointRouteBuilder MapBasemap(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/basemap").RequireAuthorization("ApiPolicy").ExcludeFromDescription();

        group.MapGet("/style.json", async Task<IResult> (string? theme, IOptions<BasemapOptions> options, HttpContext ctx, CancellationToken ct) =>
        {
            var t = (theme ?? "light").ToLowerInvariant();
            if (!Themes.Contains(t))
                return TypedResults.Problem($"Unknown theme \"{theme}\"; use light or dark.", statusCode: StatusCodes.Status400BadRequest);

            var template = ResolveTemplatePath(options.Value.AssetsPath, t);
            if (template is null) return TypedResults.NotFound();

            var json = await File.ReadAllTextAsync(template, ct);
            ctx.Response.Headers.CacheControl = "private, max-age=300";
            return TypedResults.Text(json.Replace("{BASE}", options.Value.PublicBasePath.TrimEnd('/')), "application/json");
        })
        .WithName("GetBasemapStyle");

        group.MapGet("/{**path}", IResult (string path, IOptions<BasemapOptions> options, HttpContext ctx) =>
        {
            if (TryResolveAsset(options.Value.AssetsPath, path) is not { } full) return TypedResults.NotFound();
            ctx.Response.Headers.CacheControl = "private, max-age=86400";
            return TypedResults.PhysicalFile(full, ContentType(full),
                lastModified: File.GetLastWriteTimeUtc(full), enableRangeProcessing: true);
        })
        .WithName("GetBasemap");

        return app;
    }

    /// <summary>The committed template ships next to the binary; a file of the same name under the assets volume
    /// overrides it (restyle without a rebuild).</summary>
    private static string? ResolveTemplatePath(string? assetsPath, string theme)
    {
        var name = $"style.{theme}.template.json";
        if (!string.IsNullOrWhiteSpace(assetsPath))
        {
            var overridePath = Path.Combine(assetsPath, name);
            if (File.Exists(overridePath)) return overridePath;
        }

        var bundled = Path.Combine(AppContext.BaseDirectory, "Basemap", name);
        return File.Exists(bundled) ? bundled : null;
    }

    /// <summary>Resolve a request path against the assets root; anything escaping the root (rooted paths, <c>..</c>)
    /// or missing resolves to null.</summary>
    internal static string? TryResolveAsset(string? assetsPath, string path)
    {
        if (string.IsNullOrWhiteSpace(assetsPath) || string.IsNullOrWhiteSpace(path)) return null;
        var root = Path.GetFullPath(assetsPath);
        string full;
        try
        {
            full = Path.GetFullPath(Path.Combine(root, path));
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return null;
        }

        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)) return null;
        return File.Exists(full) ? full : null;
    }

    internal static string ContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".json" => "application/json",
        ".pbf" => "application/x-protobuf",
        ".png" => "image/png",
        _ => "application/octet-stream",
    };
}
