using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LotamSentinel.Application.Common;

/// <summary>
/// Normalizes text before any equality/diff comparison. Exists specifically
/// because Phase A analysis found real cross-version "changes" that were
/// actually just an en-dash (U+2013) in one release vs. a plain hyphen in the
/// other (e.g. "WOOD POLES, ANSI CLASS HI–H3" vs "...HI-H3"). Without this
/// step the comparison engine would report false SPECIFICATION_CHANGED noise.
/// </summary>
public static class TextNormalizer
{
    private static readonly Regex WhitespaceRun = new(@"\s+", RegexOptions.Compiled);

    // Dash-family characters observed or plausible in Crystal-Reports-generated
    // text: en dash, em dash, minus sign, non-breaking hyphen -> canonicalize to '-'.
    private static readonly char[] DashVariants = { '\u2013', '\u2014', '\u2212', '\u2011' };

    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        var s = raw.Normalize(NormalizationForm.FormKC);

        foreach (var dash in DashVariants)
            s = s.Replace(dash, '-');

        s = WhitespaceRun.Replace(s, " ").Trim();

        return s;
    }

    /// <summary>Case-insensitive equality after normalization — the standard
    /// comparison used by the comparison engine for any free-text field.</summary>
    public static bool AreEquivalent(string? a, string? b)
        => string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>Aggressive normalization for supplier-name matching: strips common
    /// corporate suffixes so "ABB" / "ABB Ltd." / "ABB AG" compare closer,
    /// WITHOUT claiming they are automatically the same entity — this is only
    /// used to produce a similarity signal for the review queue, never to
    /// silently auto-merge (per the project's non-negotiable supplier rule).</summary>
    public static string NormalizeForSupplierMatch(string raw)
    {
        var s = Normalize(raw).ToUpperInvariant();
        s = Regex.Replace(s, @"\b(LTD\.?|LIMITED|LLC|INC\.?|CO\.?|COMPANY|CORP\.?|CORPORATION|AG|GMBH|S\.A\.?|SA|OY|CO\.,?\s*LTD\.?)\b", "", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"[.,]", "");
        s = WhitespaceRun.Replace(s, " ").Trim();
        return s;
    }
}
