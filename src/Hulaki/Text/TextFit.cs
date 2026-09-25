using System;
using System.Collections.Generic;

namespace Hulaki.Text;

/// <summary>Fits plain text into a <see cref="TextLimit"/> without splitting a grapheme.</summary>
public static class TextFit
{
    /// <summary>
    /// Returns <paramref name="text"/> unchanged if it fits, otherwise the longest prefix that
    /// fits with <paramref name="ellipsis"/> appended. Cuts only at grapheme boundaries, then trims
    /// trailing whitespace before the ellipsis.
    /// </summary>
    /// <param name="text">Plain text.</param>
    /// <param name="limit">The limit.</param>
    /// <param name="ellipsis">Appended when cut. Counted against the limit.</param>
    /// <returns>Text that fits.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="ellipsis"/> alone does not fit.</exception>
    public static string Truncate(string text, TextLimit limit, string ellipsis = "…")
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(limit);
        ArgumentNullException.ThrowIfNull(ellipsis);

        if (!limit.Fits(ellipsis))
        {
            throw new ArgumentException("The ellipsis alone is longer than the limit.", nameof(ellipsis));
        }

        return TruncateWhere(text, limit.Fits, ellipsis);
    }

    /// <summary>
    /// Truncates <paramref name="text"/> to the longest grapheme prefix, plus <paramref name="ellipsis"/>,
    /// that satisfies <paramref name="fits"/>. Returns the text unchanged when it already fits.
    /// </summary>
    internal static string TruncateWhere(string text, Func<string, bool> fits, string ellipsis = "…")
    {
        if (fits(text))
        {
            return text;
        }

        var boundaries = GraphemeBoundaries(text);

        // Binary search on grapheme count. Counters are monotonic (TextCounter contract).
        int low = 0, high = boundaries.Count - 1;
        while (low < high)
        {
            var mid = (low + high + 1) / 2;
            if (fits(Cut(text, boundaries[mid], ellipsis)))
            {
                low = mid;
            }
            else
            {
                high = mid - 1;
            }
        }

        return Cut(text, boundaries[low], ellipsis);
    }

    private static string Cut(string text, int length, string ellipsis) => string.Concat(text.AsSpan(0, length).TrimEnd(), ellipsis);

    /// <summary>
    /// Splits plain text into parts that each fit, breaking at paragraph, then line, then word
    /// boundaries, and only inside a word when one word is longer than the limit. For threads on
    /// social channels.
    /// </summary>
    /// <param name="text">Plain text.</param>
    /// <param name="limit">The per-part limit.</param>
    /// <returns>One or more parts, in order, none empty.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static IReadOnlyList<string> Split(string text, TextLimit limit)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(limit);

        var parts = new List<string>();
        var rest = text.Trim();
        while (rest.Length > 0)
        {
            if (limit.Fits(rest))
            {
                parts.Add(rest);
                break;
            }

            var cut = FindCut(rest, limit);
            parts.Add(rest[..cut].TrimEnd());
            rest = rest[cut..].TrimStart();
        }

        return parts;
    }

    private static int FindCut(string text, TextLimit limit)
    {
        var boundaries = GraphemeBoundaries(text);
        var fitting = 0;
        for (var i = 1; i < boundaries.Count && limit.Fits(text[..boundaries[i]]); i++)
        {
            fitting = boundaries[i];
        }

        if (fitting == 0)
        {
            // A single grapheme does not fit. Take it anyway so the loop ends; the channel will reject it.
            return boundaries.Count > 1 ? boundaries[1] : text.Length;
        }

        foreach (var separator in (ReadOnlySpan<string>)["\n\n", "\n", ". ", " "])
        {
            var at = text.LastIndexOf(separator, fitting - 1, fitting, StringComparison.Ordinal);
            if (at > 0)
            {
                return at + separator.Length;
            }
        }

        return fitting;
    }

    private static List<int> GraphemeBoundaries(string text) => GraphemeSegmenter.Boundaries(text);
}
