namespace LupiraGeoApi.Core.Application.Geocoding;

/// <summary>Reverse-geocode resolution: <c>Area</c> shares one answer per ~100 m cell, <c>Building</c> per ~11 m cell at
/// building zoom.</summary>
public enum ReversePrecision
{
    Area,
    Building,
}
