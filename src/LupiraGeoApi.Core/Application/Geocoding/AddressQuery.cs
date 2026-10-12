using Lupira.Results;
using LupiraGeoApi.Core.Domain;

namespace LupiraGeoApi.Core.Application.Geocoding;

/// <summary>A forward-geocode query: free <see cref="Text"/>, or structured address fields (never both).</summary>
public sealed record AddressQuery
{
    public string? Text { get; init; }

    /// <summary>Street name with an optional house number ("Storgatan 5 B").</summary>
    public string? Street { get; init; }

    public string? Postcode { get; init; }

    public string? Locality { get; init; }

    /// <summary>ISO 3166-1 alpha-2, upper case.</summary>
    public string? CountryCode { get; init; }

    public bool IsStructured => Text is null;

    public Guid CacheId => Text is { } t ? GeocodeCache.ForwardId(t) : GeocodeCache.StructuredForwardId(Street, Postcode, Locality, CountryCode);

    public string CacheKey => Text ?? GeocodeCache.StructuredKey(Street, Postcode, Locality, CountryCode);

    public static AddressQuery FromText(string text) => new() { Text = text.Trim() };

    public static OpResult<AddressQuery> Create(string? q, string? street, string? postcode, string? locality, string? countryCode)
    {
        (q, street, postcode, locality, countryCode) = (Clean(q), Clean(street), Clean(postcode), Clean(locality), Clean(countryCode));
        var structured = street is not null || postcode is not null || locality is not null || countryCode is not null;
        if (q is not null && structured)
            return OpResult<AddressQuery>.Invalid("Pass either q or structured fields (street, postcode, locality, countryCode), not both.");
        if (q is not null) return OpResult<AddressQuery>.Ok(new AddressQuery { Text = q });
        if (!structured) return OpResult<AddressQuery>.Invalid("Pass q or structured fields (street, postcode, locality, countryCode).");
        if (street is null && (postcode is null || locality is null))
            return OpResult<AddressQuery>.Invalid("A structured search needs street, or postcode and locality.");
        if (countryCode is not null && (countryCode.Length != 2 || !countryCode.All(char.IsAsciiLetter)))
            return OpResult<AddressQuery>.Invalid("countryCode must be an ISO 3166-1 alpha-2 code.");
        return OpResult<AddressQuery>.Ok(new AddressQuery
        {
            Street = street, Postcode = postcode, Locality = locality, CountryCode = countryCode?.ToUpperInvariant(),
        });
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
