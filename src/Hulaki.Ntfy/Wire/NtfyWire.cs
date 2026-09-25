using System.Text.Json.Serialization;

namespace Hulaki.Ntfy.Wire;

/// <summary>JSON publish body (docs.ntfy.sh/publish/#publish-as-json), posted to the server root.</summary>
internal sealed class PublishRequest
{
    [JsonPropertyName("topic")]
    public required string Topic { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    /// <summary>1 (min) to 5 (max); 3 is the default.</summary>
    [JsonPropertyName("priority")]
    public int Priority { get; init; }

    [JsonPropertyName("click")]
    public string? Click { get; init; }

    [JsonPropertyName("markdown")]
    public bool? Markdown { get; init; }
}

/// <summary>The published message. Only the id is read.</summary>
internal sealed class PublishResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("event")]
    public string? Event { get; init; }
}

/// <summary>Error body: <c>{"code":42901,"http":429,"error":"limit reached: too many requests","link":"..."}</c>.</summary>
internal sealed class NtfyError
{
    [JsonPropertyName("code")]
    public int? Code { get; init; }
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(PublishRequest))]
[JsonSerializable(typeof(PublishResponse))]
[JsonSerializable(typeof(NtfyError))]
internal sealed partial class NtfyJsonContext : JsonSerializerContext;
