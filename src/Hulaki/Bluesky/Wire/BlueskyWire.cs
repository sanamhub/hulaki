using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Hulaki.Bluesky.Wire;

/// <summary><c>com.atproto.server.createSession</c> input (docs.bsky.app/docs/api/com-atproto-server-create-session).</summary>
internal sealed class CreateSessionRequest
{
    [JsonPropertyName("identifier")]
    public required string Identifier { get; init; }

    [JsonPropertyName("password")]
    public required string Password { get; init; }
}

/// <summary>The session from <c>createSession</c> and <c>refreshSession</c>.</summary>
internal sealed class SessionResponse
{
    [JsonPropertyName("did")]
    public string? Did { get; init; }

    [JsonPropertyName("accessJwt")]
    public string? AccessJwt { get; init; }

    [JsonPropertyName("refreshJwt")]
    public string? RefreshJwt { get; init; }
}

/// <summary><c>com.atproto.repo.createRecord</c> input for an <c>app.bsky.feed.post</c>.</summary>
internal sealed class CreateRecordRequest
{
    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    [JsonPropertyName("collection")]
    public string Collection { get; init; } = "app.bsky.feed.post";

    [JsonPropertyName("record")]
    public required PostRecord Record { get; init; }
}

/// <summary>The <c>app.bsky.feed.post</c> record (atproto lexicon).</summary>
internal sealed class PostRecord
{
    [JsonPropertyName("$type")]
    public string Type { get; init; } = "app.bsky.feed.post";

    [JsonPropertyName("text")]
    public required string Text { get; init; }

    /// <summary>RFC 3339 with milliseconds and a Z, as the lexicon's datetime format asks.</summary>
    [JsonPropertyName("createdAt")]
    public required string CreatedAt { get; init; }

    [JsonPropertyName("langs")]
    public IReadOnlyList<string>? Langs { get; init; }

    [JsonPropertyName("facets")]
    public IReadOnlyList<Facet>? Facets { get; init; }

    [JsonPropertyName("reply")]
    public ReplyRef? Reply { get; init; }
}

/// <summary>A rich-text facet. Offsets count UTF-8 bytes of <see cref="PostRecord.Text"/>, end exclusive.</summary>
internal sealed class Facet
{
    [JsonPropertyName("index")]
    public required ByteSlice Index { get; init; }

    [JsonPropertyName("features")]
    public required IReadOnlyList<LinkFeature> Features { get; init; }
}

internal sealed class ByteSlice
{
    [JsonPropertyName("byteStart")]
    public int ByteStart { get; init; }

    [JsonPropertyName("byteEnd")]
    public int ByteEnd { get; init; }
}

internal sealed class LinkFeature
{
    [JsonPropertyName("$type")]
    public string Type { get; init; } = "app.bsky.richtext.facet#link";

    [JsonPropertyName("uri")]
    public required string Uri { get; init; }
}

internal sealed class ReplyRef
{
    [JsonPropertyName("root")]
    public required StrongRef Root { get; init; }

    [JsonPropertyName("parent")]
    public required StrongRef Parent { get; init; }
}

/// <summary>A <c>com.atproto.repo.strongRef</c>: also the <c>createRecord</c> output.</summary>
internal sealed class StrongRef
{
    [JsonPropertyName("uri")]
    public string? Uri { get; init; }

    [JsonPropertyName("cid")]
    public string? Cid { get; init; }
}

/// <summary>XRPC error body: <c>{"error":"ExpiredToken","message":"Token has expired"}</c>. Only the error name is kept.</summary>
internal sealed class XrpcError
{
    [JsonPropertyName("error")]
    public string? Error { get; init; }
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CreateSessionRequest))]
[JsonSerializable(typeof(SessionResponse))]
[JsonSerializable(typeof(CreateRecordRequest))]
[JsonSerializable(typeof(StrongRef))]
[JsonSerializable(typeof(XrpcError))]
internal sealed partial class BlueskyJsonContext : JsonSerializerContext;
