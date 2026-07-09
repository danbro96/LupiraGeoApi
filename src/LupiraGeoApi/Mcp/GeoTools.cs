using System.ComponentModel;
using LupiraGeoApi.Application;
using LupiraGeoApi.Auth;
using LupiraGeoApi.Dtos.Geocoding;
using LupiraGeoApi.Dtos.Places;
using LupiraGeoApi.Dtos.SavedPlaces;
using LupiraGeoApi.Mappers;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace LupiraGeoApi.Mcp;

/// <summary>Read-only geo tools for the agent — the same Core services the REST handlers use, scoped to the caller.
/// No mutations; no raw dumps beyond the gazetteer surface.</summary>
[McpServerToolType]
public sealed class GeoTools(CurrentUser user, PlaceQueryService places, GeocodingService geocoder, SavedPlaceService saved)
{
    [McpServerTool(Name = "find_places"), Description("Search the gazetteer by text and/or proximity; returns matching places with coordinates.")]
    public async Task<List<PlaceDto>> FindPlaces(
        [Description("Free-text query (place name).")] string? q = null,
        [Description("Latitude for a proximity search.")] double? nearLat = null,
        [Description("Longitude for a proximity search.")] double? nearLon = null,
        [Description("Search radius in metres (default 5000).")] double? radiusM = null,
        [Description("Max results (default 20).")] int? limit = null,
        CancellationToken ct = default) =>
        Require(await places.SearchAsync(q, null, null, null, nearLat, nearLon, radiusM, null, limit ?? 20, ct));

    [McpServerTool(Name = "get_place"), Description("Fetch a single place by id, with its containment chain.")]
    public async Task<PlaceDto> GetPlace([Description("Place id.")] Guid id, CancellationToken ct = default) =>
        Require(await places.GetAsync(id, ct));

    [McpServerTool(Name = "reverse_geocode"), Description("Resolve a coordinate to a coarse place label + structured address.")]
    public async Task<GeocodeResultDto?> ReverseGeocode(
        [Description("Latitude.")] double lat, [Description("Longitude.")] double lon, CancellationToken ct = default) =>
        (await geocoder.ReverseAsync(lat, lon, ct))?.ToDto();

    [McpServerTool(Name = "list_saved_places"), Description("List the caller's saved places / personal labels.")]
    public async Task<List<SavedPlaceDto>> ListSavedPlaces(CancellationToken ct = default)
    {
        var u = await user.GetAsync(ct);
        return Require(await saved.ListAsync(u.Id, ct));
    }

    private static T Require<T>(OpResult<T> r) => r.Status switch
    {
        OpStatus.Ok => r.Value!,
        OpStatus.NotFound => throw new McpException("Not found."),
        OpStatus.Invalid => throw new McpException(r.Error ?? "Invalid request."),
        OpStatus.Forbidden => throw new McpException(r.Error ?? "Forbidden."),
        _ => throw new McpException(r.Error ?? "Request failed."),
    };
}
