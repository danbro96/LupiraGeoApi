using LupiraGeoApi.Core.Application.Geocoding;
using LupiraGeoApi.Core.Data;
using LupiraGeoApi.Core.Domain;
using LupiraGeoApi.Core.Dtos.Places;
using Lupira.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;

namespace LupiraGeoApi.Core.Application.Places;

/// <summary>Commits a place the user picked on screen: a geocode hit (by OSM identity) or a dropped pin.</summary>
public sealed class PlaceCaptureService(
    GeoDbContext db, PlaceResolver resolver, GeocodingService geocoder, AdminAreaService adminAreas,
    ILogger<PlaceCaptureService> logger)
{
    private const double PinDedupeMeters = 25;

    /// <summary>Hit resolution: the query's frozen results, else an OSM lookup (regional, then public), else the
    /// client's copy of the hit as an approximate place. Then the resolver's dedupe and containment.</summary>
    public async Task<OpResult<ResolvePlaceResponse>> CreateFromGeocodeAsync(
        CreatePlaceFromGeocodeRequest r, Guid createdBy, CancellationToken ct = default)
    {
        if (OsmIdentity.Letter(r.OsmType) is not { } letter)
            return OpResult<ResolvePlaceResponse>.Invalid("OsmType must be node, way or relation (or N, W, R).");
        if (r is { Latitude: not null, Longitude: null } or { Latitude: null, Longitude: not null })
            return OpResult<ResolvePlaceResponse>.Invalid("Latitude and longitude must be supplied together.");

        var osmType = OsmIdentity.TypeName(r.OsmType);
        var hit = r.Query is { } query
            ? (await geocoder.FrozenForwardHitsAsync(query, ct)).FirstOrDefault(h =>
                h.OsmId == r.OsmId && string.Equals(OsmIdentity.TypeName(h.OsmType), osmType, StringComparison.Ordinal))
            : null;
        var approximate = false;
        if (hit is null)
        {
            var lookup = await geocoder.LookupAsync(letter, r.OsmId, ct);
            hit = lookup.Hits.FirstOrDefault();
            if (hit is null)
            {
                if (r is not { Latitude: { } lat, Longitude: { } lon })
                    return OpResult<ResolvePlaceResponse>.Invalid(lookup.Status == GeocodeStatus.Unavailable
                        ? "Geocoder unavailable; retry, or pass the hit's coordinates and address."
                        : "That OSM object was not found; pass the hit's coordinates and address.");
                hit = await ClientHitAsync(r, lat, lon, ct);
                approximate = true;
            }
        }

        // A client hit's locality is the reverse geocode's (for containment); its name keeps the client's address.
        var defaultName = approximate ? hit.DisplayName : PlaceNaming.FromHit(hit);
        var name = PlaceTextNormalizer.Canonical(string.IsNullOrWhiteSpace(r.Name) ? defaultName : r.Name);
        ResolveOutcome outcome;
        try
        {
            outcome = await resolver.ResolveFromHitAsync(name, hit, approximate, createdBy, ct);
        }
        catch (DbUpdateException ex)
        {
            logger.LogError(ex, "Creating a place from geocode hit {OsmType}/{OsmId} failed to persist.", osmType, r.OsmId);
            return OpResult<ResolvePlaceResponse>.Conflict("Could not persist the place; a conflicting gazetteer entry may already exist.");
        }

        return OpResult<ResolvePlaceResponse>.Ok(new ResolvePlaceResponse
        {
            Resolution = outcome.Resolution,
            PlaceId = outcome.Place?.Id,
            Name = outcome.Place?.CanonicalName ?? name,
            Latitude = outcome.Place?.Location?.Y,
            Longitude = outcome.Place?.Location?.X,
        });
    }

    /// <summary>Location is the exact pin. A geocoder outage still creates the place: the coordinates are real, and
    /// regeocode or a later edit fills the address.</summary>
    public async Task<OpResult<ResolvePlaceResponse>> CreateFromPinAsync(
        CreatePlaceFromPinRequest r, Guid createdBy, CancellationToken ct = default)
    {
        if (r.Latitude is < -90 or > 90 || r.Longitude is < -180 or > 180)
            return OpResult<ResolvePlaceResponse>.Invalid("Latitude must be within ±90 and longitude within ±180.");

        var reverse = await geocoder.ReverseAsync(r.Latitude, r.Longitude, ReversePrecision.Building, ct);
        var name = PlaceTextNormalizer.Canonical(
            string.IsNullOrWhiteSpace(r.Name) ? PlaceNaming.ForPin(reverse, r.Latitude, r.Longitude) : r.Name);
        var point = new Point(r.Longitude, r.Latitude) { SRID = 4326 };
        var key = PlaceTextNormalizer.Key(name);

        var near = await resolver.FindNearAsync(point, key, PinDedupeMeters, ct);
        if (near.FirstOrDefault(p => !HouseNumber.Conflicts(name, p.CanonicalName)) is { } match)
            return OpResult<ResolvePlaceResponse>.Ok(Response(PlaceResolution.Matched, match));

        var place = new Place
        {
            Id = Guid.NewGuid(),
            CanonicalName = name,
            NormalizedName = key,
            Kind = PlaceKind.Poi,
            Category = r.Category ?? PlaceCategory.Unknown,
            Location = point,
            FormattedAddress = reverse?.DisplayName,
            WithinAreaId = reverse is null ? null : await adminAreas.EnsureChainAsync(reverse, ct),
            Source = PlaceSource.User,
            Verified = false,
            CreatedByPrincipalId = createdBy,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Places.Add(place);
        db.Record(place.Id, CurationAction.Created, createdBy, detail: place.CanonicalName);
        await db.SaveChangesAsync(ct);
        return OpResult<ResolvePlaceResponse>.Ok(Response(PlaceResolution.Created, place));
    }

    /// <summary>The client's copy of a hit the lookup cannot confirm. Containment comes from a reverse geocode of its
    /// coordinates, so admin areas stay keyed by the canonical geocoder's names; none when that is down.</summary>
    private async Task<GeocodeHit> ClientHitAsync(CreatePlaceFromGeocodeRequest r, double lat, double lon, CancellationToken ct)
    {
        var area = await geocoder.ReverseAsync(lat, lon, ReversePrecision.Building, ct);
        var label = PlaceNaming.ShortAddress(r.Street, r.HouseNumber, r.Locality) ?? PlaceNaming.Coordinates(lat, lon);
        return new GeocodeHit(
            label, lat, lon, PlaceCategory.Unknown, area?.CountryCode, area?.Country, area?.Region, area?.Locality,
            null, null, Postcode: r.Postcode, HouseNumber: r.HouseNumber, Street: r.Street);
    }

    private static ResolvePlaceResponse Response(PlaceResolution resolution, Place place) => new()
    {
        Resolution = resolution,
        PlaceId = place.Id,
        Name = place.CanonicalName,
        Latitude = place.Location?.Y,
        Longitude = place.Location?.X,
    };
}
