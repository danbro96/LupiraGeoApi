using LupiraGeoApi.Core.Application.Geocoding;
using LupiraGeoApi.Core.Domain;
using LupiraGeoApi.Core.Dtos.AdminAreas;
using LupiraGeoApi.Core.Dtos.Geocoding;
using LupiraGeoApi.Core.Dtos.Places;
using LupiraGeoApi.Core.Dtos.SavedPlaces;

namespace LupiraGeoApi.Core.Mappers;

public static class GeoMappers
{
    public static AdminAreaDto ToDto(this AdminArea a) => new()
    {
        Id = a.Id,
        Level = a.Level,
        Name = a.Name,
        IsoCode = a.IsoCode,
        WithinAreaId = a.WithinAreaId,
        Latitude = a.Centroid?.Y,
        Longitude = a.Centroid?.X,
    };

    /// <summary>Base place → DTO. <c>Containment</c> is filled by the caller (it needs an async ancestor walk); nav
    /// collections (<c>Aliases</c>/<c>ExternalIds</c>) map only if the caller loaded them.</summary>
    public static PlaceDto ToDto(this Place p, double? distanceM = null) => new()
    {
        Id = p.Id,
        Name = p.CanonicalName,
        Kind = p.Kind,
        Category = p.Category,
        Latitude = p.Location?.Y,
        Longitude = p.Location?.X,
        FormattedAddress = p.FormattedAddress,
        Source = p.Source,
        Verified = p.Verified,
        WithinAreaId = p.WithinAreaId,
        DistanceM = distanceM,
        Aliases = p.Aliases.Select(x => new PlaceAliasDto { Id = x.Id, Name = x.Name, Lang = x.Lang }).ToList(),
        ExternalIds = p.ExternalIds.Select(x => new PlaceExternalIdDto { Scheme = x.Scheme, Value = x.Value }).ToList(),
    };

    public static SavedPlaceDto ToDto(this SavedPlace s) => new()
    {
        Id = s.Id,
        PlaceId = s.PlaceId,
        Latitude = s.RawLat,
        Longitude = s.RawLon,
        Label = s.Label,
        Notes = s.Notes,
        IsFavorite = s.IsFavorite,
    };

    public static GeocodeResultDto ToDto(this GeocodeHit h) => new()
    {
        DisplayName = h.DisplayName,
        Latitude = h.Lat,
        Longitude = h.Lon,
        Category = h.Category,
        CountryCode = h.CountryCode,
        Country = h.Country,
        Region = h.Region,
        Locality = h.Locality,
        OsmType = h.OsmType,
        OsmId = h.OsmId,
    };
}
