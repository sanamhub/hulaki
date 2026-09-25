using Hulaki.Markup;
using Hulaki.Text;
using Xunit;

namespace Hulaki.Tests;

public sealed class TextAndMarkupTests
{
    [Theory]
    [InlineData("abc", 3, 3, 3)]
    [InlineData("\U0001F468‍\U0001F469‍\U0001F467", 1, 8, 18)] // family emoji: one grapheme, 8 UTF-16 units, 18 bytes
    [InlineData("क्षि", 1, 4, 12)] // Devanagari kshi: one grapheme
    [InlineData("नमस्ते", 3, 6, 18)] // Devanagari namaste
    [InlineData("ক্ষ", 1, 3, 9)] // Bengali ksha
    [InlineData("🇳🇵", 1, 4, 8)] // flag of Nepal
    [InlineData("é", 1, 2, 3)] // e with combining acute
    public void Counters_count_like_platforms(string text, int graphemes, int utf16, int bytes)
    {
        Assert.Equal(graphemes, TextCounter.Graphemes.Count(text));
        Assert.Equal(utf16, TextCounter.Utf16CodeUnits.Count(text));
        Assert.Equal(bytes, TextCounter.Utf8Bytes.Count(text));
    }

    [Fact]
    public void Truncate_never_splits_a_grapheme()
    {
        var text = "क्षिक्षिक्षि"; // three kshi
        var limit = new TextLimit(6, TextCounter.Utf16CodeUnits);

        var cut = TextFit.Truncate(text, limit);

        Assert.Equal("क्षि…", cut); // one whole grapheme plus ellipsis, not four code units of two
    }

    [Fact]
    public void Split_prefers_paragraph_then_word_boundaries()
    {
        var parts = TextFit.Split("first para\n\nsecond one is longer", new TextLimit(15, TextCounter.Graphemes));

        Assert.Equal(["first para", "second one is", "longer"], parts);
    }

    [Theory]
    [InlineData(5)] // below one word: cuts inside words, where StringInfo alone would leave a virama
    [InlineData(12)]
    [InlineData(35)]
    public void Split_never_ends_a_devanagari_part_on_a_virama(int max)
    {
        // Conjunct-heavy Nepali. No word ends in a virama, so any part that does was cut mid-conjunct.
        const string paragraph = "क्षेत्रीय स्वास्थ्य कार्यालयले प्रदेशभर वर्षाको पूर्वानुमान र सतर्कता जारी गर्‍यो। "
            + "कृपया नदी किनार नजानुहोस्।";

        var parts = TextFit.Split(paragraph, new TextLimit(max, TextCounter.Utf16CodeUnits));

        Assert.True(parts.Count > 1);
        Assert.All(parts, part => Assert.False(part.EndsWith((char)0x094D), $"part ends in a virama: {part}"));
    }

    [Fact]
    public void Markup_parses_the_subset_and_renders_plain_text()
    {
        var doc = MarkupDocument.Parse("**Red** alert in _Myagdi_: see [DHM](https://dhm.gov.np) `code`");

        Assert.IsType<BoldNode>(doc.Nodes[0]);
        Assert.Equal("Red alert in Myagdi: see DHM (https://dhm.gov.np) code", doc.ToPlainText());
    }

    [Theory]
    [InlineData("[x](javascript:alert(1))")]
    [InlineData("**unclosed")]
    [InlineData("a_b")]
    [InlineData("[relative](/path)")]
    public void Unsafe_or_unmatched_markup_stays_literal(string source)
    {
        Assert.Equal(source, MarkupDocument.Parse(source).ToPlainText());
    }

    [Fact]
    public void Backslash_escapes_markers()
    {
        Assert.Equal("*not bold* and snake_case_name", MarkupDocument.Parse(@"\*not bold\* and snake\_case\_name").ToPlainText());
    }
}
