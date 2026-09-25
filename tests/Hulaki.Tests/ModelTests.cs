using System;
using Hulaki.Text;
using Xunit;

namespace Hulaki.Tests;

public sealed class ModelTests
{
    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Message_rejects_empty_or_whitespace_text(string text)
    {
        Assert.Throws<ArgumentException>(() => new Message(text));
    }

    [Fact]
    public void Message_rejects_null_text()
    {
        Assert.Throws<ArgumentNullException>(() => new Message(null!));
    }

    [Fact]
    public void Message_with_empty_text_throws()
    {
        var message = new Message("hello");

        Assert.Throws<ArgumentException>(() => message with { Text = "" });
    }

    [Theory]
    [InlineData("http://example.com/a.png")]
    [InlineData("images/a.png")] // "/images/a.png" would parse as an absolute file URI on Linux
    public void Media_from_url_rejects_non_https_and_relative_urls(string source)
    {
        var uri = new Uri(source, UriKind.RelativeOrAbsolute);

        Assert.Throws<ArgumentException>(() => MediaAttachment.FromUrl(uri, "image/png"));
    }

    [Fact]
    public void Media_from_url_accepts_https_and_uses_the_url_as_fingerprint()
    {
        var media = MediaAttachment.FromUrl(new Uri("https://example.com/a.png"), "image/png");

        Assert.Equal(MediaSourceKind.HttpsUrl, media.Kind);
        Assert.Equal("https://example.com/a.png", media.Fingerprint);
    }

    [Fact]
    public void Media_from_bytes_fingerprint_is_stable_for_equal_bytes()
    {
        var a = MediaAttachment.FromBytes(new byte[] { 1, 2, 3 }, "image/png");
        var b = MediaAttachment.FromBytes(new byte[] { 1, 2, 3 }, "image/png");

        Assert.Equal(a.Fingerprint, b.Fingerprint);
        Assert.StartsWith("sha256:", a.Fingerprint, StringComparison.Ordinal);
    }

    [Fact]
    public void Media_from_bytes_fingerprint_differs_for_different_bytes()
    {
        var a = MediaAttachment.FromBytes(new byte[] { 1, 2, 3 }, "image/png");
        var b = MediaAttachment.FromBytes(new byte[] { 1, 2, 4 }, "image/png");

        Assert.NotEqual(a.Fingerprint, b.Fingerprint);
    }

    [Fact]
    public void Recipient_to_string_never_contains_the_address()
    {
        var recipient = new Recipient("+9779800000001");

        Assert.DoesNotContain("9800000001", recipient.ToString(), StringComparison.Ordinal);
        Assert.Equal("Recipient(***)", recipient.ToString());
    }

    [Fact]
    public void Recipient_self_is_self()
    {
        Assert.True(Recipient.Self.IsSelf);
        Assert.Equal("Recipient(self)", Recipient.Self.ToString());
        Assert.False(new Recipient("1").IsSelf);
    }

    [Fact]
    public void Manifest_rejects_a_duplicate_declaration()
    {
        Assert.Throws<ArgumentException>(() => new CapabilityManifest(
            "dup",
            new TextLimit(10, TextCounter.Graphemes),
            [new(Capability.Text, Availability.Available), new(Capability.Text, Availability.NotImplemented)]));
    }

    [Fact]
    public void Manifest_reads_an_undeclared_capability_as_not_implemented()
    {
        var manifest = new CapabilityManifest(
            "one",
            new TextLimit(10, TextCounter.Graphemes),
            [new(Capability.Text, Availability.Available)]);

        Assert.Equal(Availability.NotImplemented, manifest.Get(Capability.Delete));
        Assert.False(manifest.Supports(Capability.Delete));
        Assert.True(manifest.Supports(Capability.Text));
    }
}
