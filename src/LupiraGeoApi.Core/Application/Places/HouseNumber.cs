using System.Text.RegularExpressions;
using LupiraGeoApi.Core.Application.Geocoding;

namespace LupiraGeoApi.Core.Application.Places;

/// <summary>The house number a place label carries ("Storgatan 5 B, lgh 1102" → <c>5B</c>), so dedupe never folds two
/// addresses on one street together.</summary>
public static partial class HouseNumber
{
    public static string? Find(string text)
    {
        foreach (var segment in text.Split(',', StringSplitOptions.TrimEntries))
        {
            if (StreetSegment().Match(segment) is { Success: true } m)
                return m.Groups["n"].Value + m.Groups["l"].Value.ToUpperInvariant();
        }

        return null;
    }

    public static bool Conflicts(string a, string b) =>
        Find(a) is { } x && Find(b) is { } y && x != y;

    /// <summary>The hit does not pin the house number <paramref name="requested"/> carries: a different number, or no
    /// number on a street-or-coarser hit. A venue hit without a number still counts as exact.</summary>
    public static bool IsApproximateFix(string requested, GeocodeHit hit)
    {
        if (Find(requested) is not { } wanted) return false;
        return hit.HouseNumber is { } got ? Normalize(got) != wanted : hit.IsCoarse;
    }

    private static string Normalize(string number) =>
        BareNumber().Match(number) is { Success: true } m
            ? m.Groups["n"].Value + m.Groups["l"].Value.ToUpperInvariant()
            : string.Concat(number.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();

    // "Storgatan 5 B" or the number-first export form "5 Storgatan"; a postcode never starts a street.
    [GeneratedRegex(@"^(?!(?:lgh|lägenhet)\b)(?:(?:\p{L}[\p{L}\p{M}.'-]*\s+)+(?<n>\d{1,4})(?:\s?(?<l>\p{L}))?(?![\p{L}\p{N}])|(?<n>\d{1,4})(?!\d)(?:\s?(?<l>\p{L}))?\s+\p{L}{2,})", RegexOptions.IgnoreCase)]
    private static partial Regex StreetSegment();

    [GeneratedRegex(@"^\s*(?<n>\d{1,4})\s?(?<l>\p{L})?\s*$")]
    private static partial Regex BareNumber();
}
