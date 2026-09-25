using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Hulaki.Teams.Wire;

/// <summary>
/// The body the Workflows template "Post to a channel when a webhook request is received" expects:
/// a message with one Adaptive Card attachment.
/// </summary>
internal sealed class WorkflowMessage
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "message";

    [JsonPropertyName("attachments")]
    public required IReadOnlyList<CardAttachment> Attachments { get; init; }
}

internal sealed class CardAttachment
{
    [JsonPropertyName("contentType")]
    public string ContentType { get; init; } = "application/vnd.microsoft.card.adaptive";

    [JsonPropertyName("content")]
    public required AdaptiveCard Content { get; init; }
}

internal sealed class AdaptiveCard
{
    [JsonPropertyName("$schema")]
    public string Schema { get; init; } = "http://adaptivecards.io/schemas/adaptive-card.json";

    [JsonPropertyName("type")]
    public string Type { get; init; } = "AdaptiveCard";

    [JsonPropertyName("version")]
    public string Version { get; init; } = "1.4";

    [JsonPropertyName("body")]
    public required IReadOnlyList<TextBlock> Body { get; init; }

    [JsonPropertyName("actions")]
    public IReadOnlyList<OpenUrlAction>? Actions { get; init; }
}

internal sealed class TextBlock
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "TextBlock";

    [JsonPropertyName("text")]
    public required string Text { get; init; }

    [JsonPropertyName("wrap")]
    public bool Wrap { get; init; } = true;

    [JsonPropertyName("weight")]
    public string? Weight { get; init; }

    [JsonPropertyName("size")]
    public string? Size { get; init; }
}

internal sealed class OpenUrlAction
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "Action.OpenUrl";

    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("url")]
    public required string Url { get; init; }
}

/// <summary>Logic Apps error body: <c>{"error":{"code":"...","message":"..."}}</c>. Only the code is read.</summary>
internal sealed class WorkflowError
{
    [JsonPropertyName("error")]
    public WorkflowErrorDetail? Error { get; init; }
}

internal sealed class WorkflowErrorDetail
{
    [JsonPropertyName("code")]
    public string? Code { get; init; }
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(WorkflowMessage))]
[JsonSerializable(typeof(WorkflowError))]
internal sealed partial class TeamsJsonContext : JsonSerializerContext;
