using System.Text.Json.Serialization;

namespace Hulaki.Mastodon.Wire;

/// <summary><c>POST /api/v1/statuses</c> form parameters as JSON (docs.joinmastodon.org/methods/statuses/#create).</summary>
internal sealed class StatusRequest
{
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("visibility")]
    public string? Visibility { get; init; }

    [JsonPropertyName("language")]
    public string? Language { get; init; }
}

/// <summary>The Status entity. Only the id and the public URL are read.</summary>
internal sealed class StatusResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("url")]
    public string? Url { get; init; }
}

/// <summary>The part of the v2 Instance entity that holds the limit (docs.joinmastodon.org/entities/Instance).</summary>
internal sealed class InstanceResponse
{
    [JsonPropertyName("configuration")]
    public InstanceConfiguration? Configuration { get; init; }
}

internal sealed class InstanceConfiguration
{
    [JsonPropertyName("statuses")]
    public StatusConfiguration? Statuses { get; init; }
}

internal sealed class StatusConfiguration
{
    [JsonPropertyName("max_characters")]
    public int? MaxCharacters { get; init; }
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(StatusRequest))]
[JsonSerializable(typeof(StatusResponse))]
[JsonSerializable(typeof(InstanceResponse))]
internal sealed partial class MastodonJsonContext : JsonSerializerContext;
