using System.Globalization;
using LupiraGeoApi.Core.Application.Geocoding;

namespace LupiraGeoApi.Core.Application.Places;

/// <summary>Default place names built from address components, never from what the user had typed so far.</summary>
public static class PlaceNaming
{
    /// <summary>"Storgatan 5, Karlstad"; null without a street.</summary>
    public static string? ShortAddress(string? street, string? houseNumber, string? locality)
    {
        if (string.IsNullOrWhiteSpace(street)) return null;
        var line = string.IsNullOrWhiteSpace(houseNumber) ? street.Trim() : $"{street.Trim()} {houseNumber.Trim()}";
        return string.IsNullOrWhiteSpace(locality) ? line : $"{line}, {locality.Trim()}";
    }

    /// <summary>A venue or settlement keeps its own name; an address or street is named by its components.</summary>
    public static string FromHit(GeocodeHit hit) =>
        hit.Name is { } name && !string.Equals(name, hit.Street, StringComparison.OrdinalIgnoreCase)
            ? name
            : ShortAddress(hit.Street ?? hit.Name, hit.HouseNumber, hit.Locality) ?? hit.DisplayName.Split(',')[0].Trim();

    public static string ForPin(GeocodeHit? reverse, double lat, double lon) =>
        ShortAddress(reverse?.Street, reverse?.HouseNumber, reverse?.Locality) ?? Coordinates(lat, lon);

    public static string Coordinates(double lat, double lon) =>
        string.Create(CultureInfo.InvariantCulture, $"{lat:F5}, {lon:F5}");
}
