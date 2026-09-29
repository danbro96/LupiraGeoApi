using System.Globalization;
using System.Text;

namespace LupiraGeoApi.Core.Application.Places;

/// <summary>Folds a place label into the key resolve matches on: case, diacritics, punctuation, home-country suffix.
/// Foreign country names are kept — abroad the country disambiguates instead of duplicating.</summary>
public static class PlaceTextNormalizer
{
    public static string Canonical(string text) =>
        string.Join(' ', text.Split((char[]?) null, StringSplitOptions.RemoveEmptyEntries));

    private static readonly string[] HomeCountryTokens = ["sweden", "sverige", "se"];

    public static string Key(string text)
    {
        var folded = Fold(text);
        var tokens = folded.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return folded;

        var parts = new List<string>(tokens);

        // "SE-139 74 Djurhamn": the ISO prefix on a postal code is noise the same address drops elsewhere.
        for (var i = parts.Count - 2; i >= 0; i--)
            if (parts[i] == "se" && parts[i + 1].All(char.IsDigit)) parts.RemoveAt(i);

        while (parts.Count > 1 && HomeCountryTokens.Contains(parts[^1])) parts.RemoveAt(parts.Count - 1);

        return string.Join(' ', parts);
    }

    private static string Fold(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        var pendingSeparator = false;
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(ch))
            {
                if (pendingSeparator && sb.Length > 0) sb.Append(' ');
                pendingSeparator = false;
                sb.Append(char.ToLowerInvariant(ch));
            }
            else
            {
                pendingSeparator = true;
            }
        }

        return sb.ToString();
    }
}
