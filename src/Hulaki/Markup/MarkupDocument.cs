using System;
using System.Collections.Generic;
using System.Text;

namespace Hulaki.Markup;

/// <summary>A node of parsed Hulaki markup (ADR-0008).</summary>
public abstract record MarkupNode;

/// <summary>Literal text. May contain line breaks.</summary>
/// <param name="Value">The text.</param>
public sealed record TextNode(string Value) : MarkupNode;

/// <summary><c>**bold**</c>.</summary>
/// <param name="Children">Content.</param>
public sealed record BoldNode(IReadOnlyList<MarkupNode> Children) : MarkupNode;

/// <summary><c>_italic_</c>.</summary>
/// <param name="Children">Content.</param>
public sealed record ItalicNode(IReadOnlyList<MarkupNode> Children) : MarkupNode;

/// <summary><c>`code`</c>. No formatting inside.</summary>
/// <param name="Value">The code text.</param>
public sealed record CodeNode(string Value) : MarkupNode;

/// <summary><c>[text](https://url)</c>. Only <c>http</c>, <c>https</c> and <c>mailto</c> targets are links; anything else stays literal.</summary>
/// <param name="Text">Link text, plain.</param>
/// <param name="Target">Absolute URL.</param>
public sealed record LinkNode(string Text, Uri Target) : MarkupNode;

/// <summary>
/// Parsed Hulaki markup: a small, safe subset every channel can render or degrade. Unmatched
/// markers are literal text, so parsing never fails.
/// </summary>
public sealed class MarkupDocument
{
    private MarkupDocument(IReadOnlyList<MarkupNode> nodes) => Nodes = nodes;

    /// <summary>Top-level nodes in order.</summary>
    public IReadOnlyList<MarkupNode> Nodes { get; }

    /// <summary>Parses markup. Never throws for malformed input.</summary>
    /// <param name="markup">The source.</param>
    /// <returns>The document.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="markup"/> is null.</exception>
    public static MarkupDocument Parse(string markup)
    {
        ArgumentNullException.ThrowIfNull(markup);
        var position = 0;
        return new MarkupDocument(ParseUntil(markup, ref position, terminator: null));
    }

    /// <summary>Renders the text a reader sees: markers removed, links as <c>text (url)</c> unless the text is the URL.</summary>
    /// <returns>Plain text.</returns>
    public string ToPlainText()
    {
        var builder = new StringBuilder();
        AppendPlain(builder, Nodes);
        return builder.ToString();
    }

    private static void AppendPlain(StringBuilder builder, IReadOnlyList<MarkupNode> nodes)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case TextNode t:
                    builder.Append(t.Value);
                    break;
                case CodeNode c:
                    builder.Append(c.Value);
                    break;
                case BoldNode b:
                    AppendPlain(builder, b.Children);
                    break;
                case ItalicNode i:
                    AppendPlain(builder, i.Children);
                    break;
                case LinkNode l when string.Equals(l.Text, l.Target.OriginalString, StringComparison.Ordinal):
                    builder.Append(l.Text);
                    break;
                case LinkNode l:
                    builder.Append(l.Text).Append(" (").Append(l.Target.OriginalString).Append(')');
                    break;
                default:
                    throw new InvalidOperationException($"Unknown node {node.GetType().Name}.");
            }
        }
    }

    private static List<MarkupNode> ParseUntil(string s, ref int i, string? terminator)
    {
        var nodes = new List<MarkupNode>();
        var text = new StringBuilder();

        void FlushText()
        {
            if (text.Length > 0)
            {
                nodes.Add(new TextNode(text.ToString()));
                text.Clear();
            }
        }

        while (i < s.Length)
        {
            if (terminator is not null && string.CompareOrdinal(s, i, terminator, 0, terminator.Length) == 0)
            {
                FlushText();
                i += terminator.Length;
                return nodes;
            }

            var c = s[i];
            if (c == '\\' && i + 1 < s.Length && "\\*_`[]()".Contains(s[i + 1], StringComparison.Ordinal))
            {
                text.Append(s[i + 1]);
                i += 2;
            }
            else if (c == '*' && At(s, i, "**") && TryDelimited(s, ref i, "**", out var bold))
            {
                FlushText();
                nodes.Add(new BoldNode(bold));
            }
            else if (c == '_' && TryDelimited(s, ref i, "_", out var italic))
            {
                FlushText();
                nodes.Add(new ItalicNode(italic));
            }
            else if (c == '`' && TryCode(s, ref i, out var code))
            {
                FlushText();
                nodes.Add(code);
            }
            else if (c == '[' && TryLink(s, ref i, out var link))
            {
                FlushText();
                nodes.Add(link);
            }
            else
            {
                text.Append(c);
                i++;
            }
        }

        FlushText();
        return nodes;
    }

    private static bool At(string s, int i, string token) => string.CompareOrdinal(s, i, token, 0, token.Length) == 0;

    private static bool TryDelimited(string s, ref int i, string marker, out IReadOnlyList<MarkupNode> children)
    {
        children = [];
        var close = s.IndexOf(marker, i + marker.Length, StringComparison.Ordinal);
        if (close <= i + marker.Length)
        {
            return false; // no closing marker, or empty span: literal
        }

        var inner = i + marker.Length;
        children = ParseUntil(s[..close], ref inner, terminator: null);
        i = close + marker.Length;
        return true;
    }

    private static bool TryCode(string s, ref int i, out CodeNode node)
    {
        node = null!;
        var close = s.IndexOf('`', i + 1);
        if (close <= i + 1)
        {
            return false;
        }

        node = new CodeNode(s[(i + 1)..close]);
        i = close + 1;
        return true;
    }

    private static bool TryLink(string s, ref int i, out LinkNode node)
    {
        node = null!;
        var closeText = s.IndexOf("](", i + 1, StringComparison.Ordinal);
        if (closeText < 0)
        {
            return false;
        }

        var closeUrl = s.IndexOf(')', closeText + 2);
        if (closeUrl < 0)
        {
            return false;
        }

        var text = s[(i + 1)..closeText];
        var url = s[(closeText + 2)..closeUrl];
        if (text.Length == 0
            || !Uri.TryCreate(url, UriKind.Absolute, out var target)
            || (target.Scheme != Uri.UriSchemeHttps && target.Scheme != Uri.UriSchemeHttp && target.Scheme != Uri.UriSchemeMailto))
        {
            return false; // javascript:, data:, relative: stays literal
        }

        node = new LinkNode(text, target);
        i = closeUrl + 1;
        return true;
    }
}
