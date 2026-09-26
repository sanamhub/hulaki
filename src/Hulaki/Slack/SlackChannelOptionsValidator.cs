using System;
using Microsoft.Extensions.Options;

namespace Hulaki.Slack;

/// <summary>Checks named <see cref="SlackChannelOptions"/> at startup. Never echoes the webhook URL.</summary>
internal sealed class SlackChannelOptionsValidator : IValidateOptions<SlackChannelOptions>
{
    public ValidateOptionsResult Validate(string? name, SlackChannelOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var channel = name ?? string.Empty;
        if (options.WebhookUrl is null)
        {
            return ValidateOptionsResult.Fail($"Slack channel '{channel}': WebhookUrl is empty.");
        }

        return SlackChannel.IsWebhookUrl(options.WebhookUrl)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail($"Slack channel '{channel}': WebhookUrl must be an https://hooks.slack.com/services/... URL.");
    }
}
