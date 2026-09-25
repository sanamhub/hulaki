using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Hulaki.Bluesky.Wire;
using Hulaki.Markup;

namespace Hulaki.Bluesky;

/// <summary>A link over a span of the text, in UTF-16 indexes of the .NET string.</summary>
internal readonly record struct LinkSpan(int Start, int End, string Uri);

/// <summary>
/// Post text with its links. Bluesky has no markup: bold and italic become plain text, and links
/// become facets whose offsets are UTF-8 bytes, not the UTF-16 indexes .NET strings use.
/// </summary>
internal sealed partial class RichText
{
    private RichText(string text, IReadOnlyList<LinkSpan> links)
    {
        Text = text;
        Links = links;
    }

    public string Text { get; }

    public IReadOnlyList<LinkSpan> Links { get; }

    /// <summary>The title line, the body and the message link, with every link as a span.</summary>
    public static RichText From(Message message)
    {
        var builder = new StringBuilder();
        var links = new List<LinkSpan>();
        if (message.Title is not null)
        {
            AppendPlain(builder, links, message.Title);
            builder.Append('\n');
        }

        if (message.Format == TextFormat.Markup)
        {
            AppendNodes(builder, links, MarkupDocument.Parse(message.Text).Nodes);
        }
        else
        {
            AppendPlain(builder, links, message.Text);
        }

        if (message.Link is not null)
        {
            builder.Append('\n');
            AppendLink(builder, links, message.Link.AbsoluteUri, message.Link.AbsoluteUri);
        }

        return new RichText(builder.ToString(), links);
    }

    /// <summary>
    /// The part of the text from <paramref name="start"/> for <paramref name="length"/> UTF-16 units,
    /// with the links that lie wholly inside it. A link cut by the split loses its facet.
    /// </summary>
    public RichText Slice(int start, int length)
    {
        var end = start + length;
        var inside = Links.Where(l => l.Start >= start && l.End <= end).Select(l => l with { Start = l.Start - start, End = l.End - start });
        return new RichText(Text.Substring(start, length), [.. inside]);
    }

    /// <summary>Facets with UTF-8 byte offsets, or null when there are no links.</summary>
    public IReadOnlyList<Facet>? Facets()
    {
        if (Links.Count == 0)
        {
            return null;
        }

        // string.IndexOf-style positions are UTF-16. Bluesky counts bytes of the UTF-8 encoding,
        // so a Devanagari or emoji prefix moves every offset.
        return [.. Links.Select(l => new Facet
        {
            Index = new ByteSlice
            {
                ByteStart = Encoding.UTF8.GetByteCount(Text.AsSpan(0, l.Start)),
                ByteEnd = Encoding.UTF8.GetByteCount(Text.AsSpan(0, l.End)),
            },
            Features = [new LinkFeature { Uri = l.Uri }],
        })];
    }

    private static void AppendNodes(StringBuilder builder, List<LinkSpan> links, IReadOnlyList<MarkupNode> nodes)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case TextNode t:
                    AppendPlain(builder, links, t.Value);
                    break;
                case BoldNode b:
                    AppendNodes(builder, links, b.Children);
                    break;
                case ItalicNode i:
                    AppendNodes(builder, links, i.Children);
                    break;
                case CodeNode c:
                    builder.Append(c.Value);
                    break;
                case LinkNode l:
                    AppendLink(builder, links, l.Text, l.Target.AbsoluteUri);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown node {node.GetType().Name}.");
            }
        }
    }

    // Bluesky does not link bare URLs itself; the official app detects them when composing. Do the same.
    private static void AppendPlain(StringBuilder builder, List<LinkSpan> links, string text)
    {
        var offset = builder.Length;
        builder.Append(text);
        foreach (Match match in BareUrl().Matches(text))
        {
            var value = match.Value.TrimEnd('.', ',', ';', ':', '!', '?', ')', ']', '\'', '"');
            if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
            {
                links.Add(new LinkSpan(offset + match.Index, offset + match.Index + value.Length, uri.AbsoluteUri));
            }
        }
    }

    private static void AppendLink(StringBuilder builder, List<LinkSpan> links, string text, string uri)
    {
        var start = builder.Length;
        builder.Append(text);
        links.Add(new LinkSpan(start, builder.Length, uri));
    }

    [GeneratedRegex(@"https?://[^\s<>""]+", RegexOptions.CultureInvariant)]
    private static partial Regex BareUrl();
}
