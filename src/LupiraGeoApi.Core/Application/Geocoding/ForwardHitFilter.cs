using System.Text.RegularExpressions;

namespace LupiraGeoApi.Core.Application.Geocoding;

/// <summary>Rejects forward hits the query contradicts, so the next endpoint gets asked instead of a wrong answer
/// being frozen (the regional instance returns its closest name: a Danish hamlet for "Berlin").</summary>
public static partial class ForwardHitFilter
{
    // Bare words need a notable hit: Herräng (0.38) passes at home, Șimon RO (0.42) fails abroad, Berlin (0.85) passes.
    internal const double RegionalMinImportance = 0.2;
    internal const double ForeignMinImportance = 0.5;

    public static bool Accepts(string query, GeocodeHit hit, IReadOnlySet<string> regionalCountries)
    {
        // Postal region only — OSM often files a village under a neighbouring full code.
        if (Postcode(query) is { } wanted && Digits(hit.Postcode) is { Length: >= 2 } got && wanted[..2] != got[..2])
            return false;
        if (!BareWord().IsMatch(query.Trim())) return true;
        var bar = hit.CountryCode is { } cc && regionalCountries.Contains(cc) ? RegionalMinImportance : ForeignMinImportance;
        return (hit.Importance ?? 0) >= bar;
    }

    private static string? Postcode(string query) =>
        PostcodeToken().Match(query) is { Success: true } m ? m.Groups[1].Value + m.Groups[2].Value : null;

    private static string? Digits(string? s) => s is null ? null : new string(s.Where(char.IsAsciiDigit).ToArray());

    [GeneratedRegex(@"^\p{L}+$")]
    private static partial Regex BareWord();

    [GeneratedRegex(@"(?<!\d)(\d{3}) ?(\d{2})(?!\d)")]
    private static partial Regex PostcodeToken();
}
