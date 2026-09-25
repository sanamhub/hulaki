using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Hulaki.Discord.Wire;

/// <summary>Execute Webhook body (discord.com/developers/docs/resources/webhook#execute-webhook).</summary>
internal sealed class ExecuteWebhookRequest
{
    [JsonPropertyName("content")]
    public required string Content { get; init; }

    /// <summary>An empty <c>parse</c> list: no <c>@everyone</c>, role or user mention pings, whatever the text says.</summary>
    [JsonPropertyName("allowed_mentions")]
    public AllowedMentions AllowedMentions { get; init; } = new();
}

internal sealed class AllowedMentions
{
    [JsonPropertyName("parse")]
    public IReadOnlyList<string> Parse { get; init; } = [];
}

/// <summary>The message object returned with <c>?wait=true</c>. Only the id is read.</summary>
internal sealed class DiscordMessage
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }
}

/// <summary>Error body: <c>{"code": 10015, "message": "Unknown Webhook"}</c>, or a 429's <c>retry_after</c> in seconds.</summary>
internal sealed class DiscordError
{
    [JsonPropertyName("code")]
    public int? Code { get; init; }

    [JsonPropertyName("retry_after")]
    public double? RetryAfter { get; init; }
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ExecuteWebhookRequest))]
[JsonSerializable(typeof(DiscordMessage))]
[JsonSerializable(typeof(DiscordError))]
internal sealed partial class DiscordJsonContext : JsonSerializerContext;
