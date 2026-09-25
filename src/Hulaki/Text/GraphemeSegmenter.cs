using System.Collections.Generic;
using System.Globalization;

namespace Hulaki.Text;

/// <summary>
/// Grapheme boundaries as Unicode 15.1 defines them. .NET's <see cref="StringInfo"/> implements
/// UAX #29 without rule GB9c, so it splits Indic conjuncts: Devanagari "क्षि" is two text elements
/// in .NET and one in ICU, JavaScript <c>Intl.Segmenter</c> and therefore Bluesky. Cutting there
/// leaves a dangling virama. This adds GB9c on top of <see cref="StringInfo"/> (ADR-0008).
/// </summary>
internal static class GraphemeSegmenter
{
    /// <summary>UTF-16 index after each grapheme; element 0 is 0.</summary>
    public static List<int> Boundaries(string text)
    {
        var result = new List<int> { 0 };
        var index = 0;
        while (index < text.Length)
        {
            index += StringInfo.GetNextTextElementLength(text, index);

            // GB9c: no break between Linker (virama) and a following consonant of the same script.
            while (index < text.Length && index >= 1 && IsLinker(text[index - 1]) && IsConsonantAfter(text[index - 1], text[index]))
            {
                index += StringInfo.GetNextTextElementLength(text, index);
            }

            result.Add(index);
        }

        return result;
    }

    public static int Count(string text) => Boundaries(text).Count - 1;

    // InCB=Linker in Unicode 15.1: the viramas of Devanagari, Bengali, Gujarati, Oriya, Telugu, Malayalam.
    private static bool IsLinker(char c) => c is '्' or '্' or '્' or '୍' or '్' or '്';

    private static bool IsConsonantAfter(char linker, char next) => linker switch
    {
        '्' => next is (>= 'क' and <= 'ह') or (>= 'क़' and <= 'य़') or (>= 'ॸ' and <= 'ॿ'),
        '্' => next is (>= 'ক' and <= 'হ') or 'ড়' or 'ঢ়' or 'য়' or 'ৰ' or 'ৱ',
        '્' => next is >= 'ક' and <= 'હ',
        '୍' => next is (>= 'କ' and <= 'ହ') or 'ଡ଼' or 'ଢ଼' or 'ୟ' or 'ୱ',
        '్' => next is >= 'క' and <= 'హ',
        '്' => next is >= 'ക' and <= 'ഺ',
        _ => false,
    };
}
