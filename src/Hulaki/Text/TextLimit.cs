using System;
using System.Text;

namespace Hulaki.Text;

/// <summary>
/// Counts text the way a platform does. Platforms disagree: Bluesky counts graphemes, Telegram
/// counts UTF-16 code units, X weights code points, SMS counts segments (ADR-0008).
/// </summary>
public abstract class TextCounter
{
    /// <summary>User-perceived characters: UAX #29 extended grapheme clusters with the Unicode 15.1 Indic conjunct rule, as ICU and Bluesky count them.</summary>
    public static TextCounter Graphemes { get; } = new GraphemeCounter();

    /// <summary>UTF-16 code units, which is <see cref="string.Length"/>.</summary>
    public static TextCounter Utf16CodeUnits { get; } = new Utf16Counter();

    /// <summary>UTF-8 bytes.</summary>
    public static TextCounter Utf8Bytes { get; } = new Utf8Counter();

    /// <summary>Short name for docs and diagnostics, for example <c>graphemes</c>.</summary>
    public abstract string Unit { get; }

    /// <summary>Counts <paramref name="text"/>. Must be monotonic: a prefix never counts more than the whole.</summary>
    /// <param name="text">The rendered text as the platform will receive it.</param>
    /// <returns>The count.</returns>
    public abstract int Count(string text);

    private sealed class GraphemeCounter : TextCounter
    {
        public override string Unit => "graphemes";

        public override int Count(string text) => GraphemeSegmenter.Count(text);
    }

    private sealed class Utf16Counter : TextCounter
    {
        public override string Unit => "utf16";

        public override int Count(string text) => text.Length;
    }

    private sealed class Utf8Counter : TextCounter
    {
        public override string Unit => "bytes";

        public override int Count(string text) => Encoding.UTF8.GetByteCount(text);
    }
}

/// <summary>A maximum text length and the counter that measures it.</summary>
/// <param name="Max">Maximum count, inclusive.</param>
/// <param name="Counter">How to count.</param>
public sealed record TextLimit(int Max, TextCounter Counter)
{
    /// <summary>True when <paramref name="text"/> fits.</summary>
    /// <param name="text">Rendered text.</param>
    /// <returns>Whether the count is at most <see cref="Max"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    public bool Fits(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Counter.Count(text) <= Max;
    }
}
