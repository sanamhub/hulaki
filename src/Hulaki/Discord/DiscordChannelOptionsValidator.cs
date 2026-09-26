using System;
using Microsoft.Extensions.Options;

namespace Hulaki.Discord;

/// <summary>Checks named <see cref="DiscordChannelOptions"/> at startup. Never echoes the webhook URL.</summary>
internal sealed class DiscordChannelOptionsValidator : IValidateOptions<DiscordChannelOptions>
{
    public ValidateOptionsResult Validate(string? name, DiscordChannelOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var channel = name ?? string.Empty;
        if (options.WebhookUrl is null)
        {
            return ValidateOptionsResult.Fail($"Discord channel '{channel}': WebhookUrl is empty.");
        }

        return DiscordChannel.IsWebhookUrl(options.WebhookUrl)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail($"Discord channel '{channel}': WebhookUrl must be an https://discord.com/api/webhooks/... or https://discordapp.com/api/webhooks/... URL.");
    }
}
