using System.ComponentModel;
using LupiraGeoApi.Application;
using LupiraGeoApi.Auth;
using LupiraGeoApi.Domain;
using LupiraGeoApi.Dtos.Geocoding;
using LupiraGeoApi.Dtos.Places;
using LupiraGeoApi.Dtos.SavedPlaces;
using LupiraGeoApi.Mappers;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace LupiraGeoApi.Mcp;

/// <summary>Geo tools for the agent — the same Core services the REST handlers use, scoped to the caller. Reads cover
/// search/lookup/reverse-geocode; writes cover the import + curation path (forward-geocode, resolve, create, save,
/// alias, curate). LAN/WireGuard-only (see <see cref="LupiraGeoApi.Endpoints.McpExposure"/>), so no public write surface.</summary>
[McpServerToolType]
public sealed class GeoTools(CurrentUser user, PlaceQueryService places, GeocodingService geocoder, PlaceMergeService merges, SavedPlaceService saved)
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

    [McpServerTool(Name = "forward_geocode"), Description("Resolve free text (address/place) to candidate coordinates + structured address. Does NOT persist a place — use for private homes you want to keep out of the shared gazetteer (feed the coordinate to save_place). Returns an empty list on both a genuine no-hit and a transient geocoder outage; retry an empty result before treating it as 'not found'.")]
    public async Task<List<GeocodeResultDto>> ForwardGeocode(
        [Description("Text to geocode (e.g. a street address).")] string q,
        [Description("Max candidates (default 5).")] int? limit = null,
        CancellationToken ct = default) =>
        (await geocoder.ForwardAsync(q, limit ?? 5, ct)).Hits.Select(h => h.ToDto()).ToList();

    [McpServerTool(Name = "list_saved_places"), Description("List the caller's saved places / personal labels.")]
    public async Task<List<SavedPlaceDto>> ListSavedPlaces(CancellationToken ct = default)
    {
        var u = await user.GetAsync(ct);
        return Require(await saved.ListAsync(u.Id, ct));
    }

    [McpServerTool(Name = "resolve_place"), Description("Resolve free text to a gazetteer place id — matches an existing entry, else forward-geocodes and creates one, else provisionally creates an unverified place with no coordinates. Use for shared POIs (schools, workplaces, parks). The 'resolution' field says which happened (Matched/Geocoded/Provisional/GeocodeUnavailable); GeocodeUnavailable means the geocoder was unreachable and NOTHING was created (placeId null) — retry it, don't treat it as not-found. Heal a Provisional stub later with regeocode_place.")]
    public async Task<ResolvePlaceResponse> ResolvePlace(
        [Description("Free-text place/address to resolve.")] string text, CancellationToken ct = default)
    {
        var u = await user.GetAsync(ct);
        return Require(await places.ResolveAsync(text, u.Id, ct));
    }

    [McpServerTool(Name = "resolve_places"), Description("Bulk resolve (max 50 texts) for imports; responses align index-for-index with the input. Aborts only on invalid input (blank text); a per-item geocoder outage comes back as resolution=GeocodeUnavailable (placeId null) so the batch still completes — re-run just those items, spaced out.")]
    public async Task<List<ResolvePlaceResponse>> ResolvePlaces(
        [Description("Texts to resolve (max 50).")] List<string> texts, CancellationToken ct = default)
    {
        var u = await user.GetAsync(ct);
        return Require(await places.ResolveBatchAsync(texts, u.Id, ct));
    }

    [McpServerTool(Name = "create_place"), Description("Create a gazetteer place directly with a known name/category and optional coordinates. Prefer when you already know the semantics (e.g. a school); use resolve_place when you only have free text.")]
    public async Task<PlaceDto> CreatePlace(
        [Description("Canonical place name.")] string name,
        [Description("Poi (a named venue) or Address.")] PlaceKind kind = PlaceKind.Poi,
        [Description("Semantic category (Home/Office/School/…).")] PlaceCategory category = PlaceCategory.Unknown,
        [Description("Latitude (optional; omit for a coordinate-less provisional place).")] double? latitude = null,
        [Description("Longitude (optional).")] double? longitude = null,
        [Description("Formatted address (optional).")] string? formattedAddress = null,
        [Description("Containing AdminArea id (optional).")] Guid? withinAreaId = null,
        CancellationToken ct = default)
    {
        var u = await user.GetAsync(ct);
        return Require(await places.CreateAsync(new CreatePlaceRequest
        {
            Name = name, Kind = kind, Category = category,
            Latitude = latitude, Longitude = longitude,
            FormattedAddress = formattedAddress, WithinAreaId = withinAreaId,
        }, u.Id, ct));
    }

    [McpServerTool(Name = "update_place"), Description("Curate a place: rename, recategorize, verify, or correct its location by hand. Omitted fields are left unchanged. latitude+longitude (both together) move the point — use to fix a wrong geocode; pass withinAreaId to re-anchor its containment to match. To auto-heal a coordinate-less place from its address, prefer regeocode_place.")]
    public async Task<PlaceDto> UpdatePlace(
        [Description("Place id.")] Guid id,
        [Description("New canonical name (optional).")] string? name = null,
        [Description("New category (optional).")] PlaceCategory? category = null,
        [Description("Verified flag (optional).")] bool? verified = null,
        [Description("Corrected latitude (optional; must accompany longitude).")] double? latitude = null,
        [Description("Corrected longitude (optional; must accompany latitude).")] double? longitude = null,
        [Description("Formatted address (optional).")] string? formattedAddress = null,
        [Description("Containing AdminArea id to re-anchor containment (optional).")] Guid? withinAreaId = null,
        CancellationToken ct = default)
    {
        var u = await user.GetAsync(ct);
        return Require(await places.UpdateAsync(id, new UpdatePlaceRequest
        {
            Name = name, Category = category, Verified = verified,
            Latitude = latitude, Longitude = longitude, FormattedAddress = formattedAddress, WithinAreaId = withinAreaId,
        }, u.Id, ct));
    }

    [McpServerTool(Name = "regeocode_place"), Description("Re-run geocoding for an existing place from its address/name and attach the coordinates, containment chain, and OSM id — heals a coordinate-less provisional stub (or refreshes a stale fix). Leaves the place unchanged on a no-hit or a transient geocoder outage.")]
    public async Task<PlaceDto> RegeocodePlace([Description("Place id.")] Guid id, CancellationToken ct = default)
    {
        var u = await user.GetAsync(ct);
        return Require(await places.RegeocodeAsync(id, u.Id, ct));
    }

    [McpServerTool(Name = "merge_places"), Description("Merge a duplicate place into the survivor (intoPlaceId): the duplicate's names become aliases, its external ids and saved places move over, and the duplicate id keeps resolving via a tombstone redirect. Use for genuine duplicates — for a WRONG entry with no correct survivor, use delete_place instead (merge would drag the wrong external ids onto the survivor).")]
    public async Task<PlaceDto> MergePlaces(
        [Description("The duplicate to merge away.")] Guid sourceId,
        [Description("The survivor to merge into.")] Guid intoPlaceId,
        CancellationToken ct = default)
    {
        var u = await user.GetAsync(ct);
        return Require(await merges.MergeAsync(sourceId, intoPlaceId, u.Id, ct));
    }

    [McpServerTool(Name = "delete_place"), Description("Soft-delete a bad gazetteer entry (e.g. a wrong geocode) with no valid survivor to merge into. Tombstoned: reads 404 and search/resolve exclude it, but the row stays for the audit trail. Idempotent.")]
    public async Task<string> DeletePlace([Description("Place id.")] Guid id, CancellationToken ct = default)
    {
        var u = await user.GetAsync(ct);
        RequireOk(await places.DeleteAsync(id, u.Id, ct));
        return $"Deleted {id}.";
    }

    [McpServerTool(Name = "add_place_alias"), Description("Add an alternate name (optional language tag) to a place — a translation, colloquialism, or former name.")]
    public async Task<PlaceDto> AddPlaceAlias(
        [Description("Place id.")] Guid id,
        [Description("Alternate name.")] string name,
        [Description("BCP-47 language tag (optional, e.g. 'sv').")] string? lang = null,
        CancellationToken ct = default)
    {
        var u = await user.GetAsync(ct);
        return Require(await places.AddAliasAsync(id, new AddAliasRequest { Name = name, Lang = lang }, u.Id, ct));
    }

    [McpServerTool(Name = "add_place_external_id"), Description("Attach an external gazetteer id (OSM way/node/relation, Wikidata Q-id, Google place id, GeoNames id) to a place so imports/dedup reconcile against it. Multiple ids per scheme are allowed. 409 if that id already belongs to another place (merge those instead) or is already on this place.")]
    public async Task<PlaceDto> AddPlaceExternalId(
        [Description("Place id.")] Guid id,
        [Description("External scheme: Osm, Wikidata, Google, or Geonames.")] ExternalScheme scheme,
        [Description("External id value, e.g. 'way/54739745', 'node/123', 'Q42'.")] string value,
        CancellationToken ct = default)
    {
        var u = await user.GetAsync(ct);
        return Require(await places.AddExternalIdAsync(id, new AddExternalIdRequest { Scheme = scheme, Value = value }, u.Id, ct));
    }

    [McpServerTool(Name = "remove_place_external_id"), Description("Detach an external id (scheme+value) from a place — e.g. clear a stale OSM id before attaching the correct one. Returns the updated place. Not-found if the place has no such id.")]
    public async Task<PlaceDto> RemovePlaceExternalId(
        [Description("Place id.")] Guid id,
        [Description("External scheme: Osm, Wikidata, Google, or Geonames.")] ExternalScheme scheme,
        [Description("External id value to remove, e.g. 'way/6601741'.")] string value,
        CancellationToken ct = default)
    {
        var u = await user.GetAsync(ct);
        RequireOk(await places.RemoveExternalIdAsync(id, scheme, value, u.Id, ct));
        return Require(await places.GetAsync(id, ct));
    }

    [McpServerTool(Name = "save_place"), Description("Save a personal label (private, owner-scoped) over a gazetteer place id, or over a raw coordinate. This is where 'Home', 'Work', family homes live — not the shared catalog.")]
    public async Task<SavedPlaceDto> SavePlace(
        [Description("Personal label, e.g. 'Home' or 'Mormor & morfar'.")] string label,
        [Description("Gazetteer place id to label (optional if lat/lon given).")] Guid? placeId = null,
        [Description("Raw latitude (optional; use for a private home kept out of the gazetteer).")] double? latitude = null,
        [Description("Raw longitude (optional).")] double? longitude = null,
        [Description("Icon hint (optional).")] string? icon = null,
        [Description("Free-text note (optional), e.g. 'longest childhood home'.")] string? notes = null,
        [Description("Mark as favorite (default false).")] bool isFavorite = false,
        CancellationToken ct = default)
    {
        var u = await user.GetAsync(ct);
        return Require(await saved.CreateAsync(u.Id, new CreateSavedPlaceRequest
        {
            Label = label, PlaceId = placeId, Latitude = latitude, Longitude = longitude,
            Icon = icon, Notes = notes, IsFavorite = isFavorite,
        }, ct));
    }

    [McpServerTool(Name = "update_saved_place"), Description("Update one of the caller's saved places (owner-scoped): rename, re-icon, annotate, (un)favorite, or re-point it. Omitted fields are left unchanged. Re-point by passing EITHER placeId (link a gazetteer place; clears any raw coordinate) OR latitude+longitude together (set a raw coordinate; clears any link) — not both. Not-found if the id isn't yours.")]
    public async Task<SavedPlaceDto> UpdateSavedPlace(
        [Description("Saved place id (from list_saved_places).")] Guid id,
        [Description("New label (optional).")] string? label = null,
        [Description("Re-point to this gazetteer place id (optional; clears raw coordinate).")] Guid? placeId = null,
        [Description("New raw latitude (optional; must accompany longitude; clears place link).")] double? latitude = null,
        [Description("New raw longitude (optional; must accompany latitude).")] double? longitude = null,
        [Description("Icon hint (optional).")] string? icon = null,
        [Description("Free-text note (optional).")] string? notes = null,
        [Description("Favorite flag (optional).")] bool? isFavorite = null,
        CancellationToken ct = default)
    {
        var u = await user.GetAsync(ct);
        return Require(await saved.UpdateAsync(u.Id, id, new UpdateSavedPlaceRequest
        {
            Label = label, PlaceId = placeId, Latitude = latitude, Longitude = longitude,
            Icon = icon, Notes = notes, IsFavorite = isFavorite,
        }, ct));
    }

    [McpServerTool(Name = "delete_saved_place"), Description("Delete one of the caller's saved places (owner-scoped). Not-found if the id isn't yours.")]
    public async Task<string> DeleteSavedPlace([Description("Saved place id (from list_saved_places).")] Guid id, CancellationToken ct = default)
    {
        var u = await user.GetAsync(ct);
        RequireOk(await saved.DeleteAsync(u.Id, id, ct));
        return $"Deleted saved place {id}.";
    }

    private static T Require<T>(OpResult<T> r) => r.Status switch
    {
        OpStatus.Ok => r.Value!,
        OpStatus.NotFound => throw new McpException("Not found."),
        OpStatus.Invalid => throw new McpException(r.Error ?? "Invalid request."),
        OpStatus.Forbidden => throw new McpException(r.Error ?? "Forbidden."),
        _ => throw new McpException(r.Error ?? "Request failed."),
    };

    private static void RequireOk(OpResult r)
    {
        if (r.Status == OpStatus.Ok) return;
        throw r.Status switch
        {
            OpStatus.NotFound => new McpException("Not found."),
            OpStatus.Invalid => new McpException(r.Error ?? "Invalid request."),
            OpStatus.Forbidden => new McpException(r.Error ?? "Forbidden."),
            _ => new McpException(r.Error ?? "Request failed."),
        };
    }
}
