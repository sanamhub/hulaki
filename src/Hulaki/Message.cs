using System;
using System.Collections.Generic;

namespace Hulaki;

/// <summary>How <see cref="Message.Text"/> is interpreted.</summary>
public enum TextFormat
{
    /// <summary>Literal text. Nothing is escaped or rendered.</summary>
    Plain = 0,

    /// <summary>Hulaki markup (ADR-0008): <c>**bold**</c>, <c>_italic_</c>, <c>`code`</c>, <c>[text](https://url)</c>.</summary>
    Markup = 1,
}

/// <summary>Delivery urgency. Channels map it to their own notion (ntfy priority, Telegram silent sends, Web Push urgency).</summary>
public enum MessagePriority
{
    /// <summary>No sound or badge where the channel allows it.</summary>
    Low = 0,

    /// <summary>The channel default.</summary>
    Normal = 1,

    /// <summary>Above default.</summary>
    High = 2,

    /// <summary>Highest the channel offers. Use for warnings people must act on.</summary>
    Urgent = 3,
}

/// <summary>What to do when the text is longer than the channel allows.</summary>
public enum OverflowBehavior
{
    /// <summary>Do not send. The outcome is <see cref="DeliveryStatus.NotSubmitted"/> with a <c>text-too-long</c> issue.</summary>
    Reject = 0,

    /// <summary>Cut the text at a grapheme boundary and append an ellipsis. Plain text only in 0.x.</summary>
    Truncate = 1,
}

/// <summary>
/// The content to deliver. Immutable. One <see cref="Message"/> can be sent to many targets.
/// </summary>
public sealed record Message
{
    private readonly string _text = string.Empty;

    /// <summary>Creates a message.</summary>
    /// <param name="text">The body. Must not be empty or whitespace.</param>
    /// <exception cref="ArgumentException"><paramref name="text"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    public Message(string text)
    {
        Text = text;
    }

    /// <summary>The body, interpreted according to <see cref="Format"/>. Must not be empty or whitespace.</summary>
    /// <exception cref="ArgumentException">Set to an empty or whitespace value.</exception>
    /// <exception cref="ArgumentNullException">Set to null.</exception>
    public string Text
    {
        get => _text;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            _text = value;
        }
    }

    /// <summary>How <see cref="Text"/> is interpreted. Defaults to <see cref="TextFormat.Plain"/>.</summary>
    public TextFormat Format { get; init; }

    /// <summary>Optional heading. Channels without titles prepend it to the body on its own line.</summary>
    public string? Title { get; init; }

    /// <summary>Delivery urgency. Defaults to <see cref="MessagePriority.Normal"/>.</summary>
    public MessagePriority Priority { get; init; } = MessagePriority.Normal;

    /// <summary>Primary link. Becomes the click action where the channel has one, otherwise it is appended.</summary>
    public Uri? Link { get; init; }

    /// <summary>Attachments, in order. Empty by default.</summary>
    public IReadOnlyList<MediaAttachment> Media { get; init; } = [];

    /// <summary>What to do when the text is too long. Defaults to <see cref="OverflowBehavior.Reject"/>.</summary>
    public OverflowBehavior Overflow { get; init; }

    /// <summary>
    /// Caller key that makes a repeated send of the same message a no-op (ADR-0005). Null means no
    /// deduplication.
    /// </summary>
    public string? IdempotencyKey { get; init; }

    /// <summary>Returns a copy whose plain body is cut so <paramref name="counted"/> of the copy fits <paramref name="limit"/>.</summary>
    internal Message WithTruncatedText(Hulaki.Text.TextLimit limit, Func<Message, string> counted) =>
        this with { Text = Hulaki.Text.TextFit.TruncateWhere(Text, candidate => limit.Fits(counted(this with { Text = candidate }))) };
}
