using System;
using Microsoft.Extensions.Options;

namespace Hulaki.Telegram;

/// <summary>Checks named <see cref="TelegramChannelOptions"/> at startup. Never echoes the token.</summary>
internal sealed class TelegramChannelOptionsValidator : IValidateOptions<TelegramChannelOptions>
{
    public ValidateOptionsResult Validate(string? name, TelegramChannelOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var channel = name ?? string.Empty;
        if (string.IsNullOrWhiteSpace(options.BotToken))
        {
            return ValidateOptionsResult.Fail($"Telegram channel '{channel}': BotToken is empty.");
        }

        // HTTPS only, except a local Bot API server on loopback (ADR-0015).
        var address = options.BaseAddress;
        var allowed = address is { IsAbsoluteUri: true }
            && (address.Scheme == Uri.UriSchemeHttps || (address.Scheme == Uri.UriSchemeHttp && address.IsLoopback));
        if (!allowed)
        {
            return ValidateOptionsResult.Fail($"Telegram channel '{channel}': BaseAddress must be an absolute HTTPS URL, or an HTTP loopback URL for a local server.");
        }

        return ValidateOptionsResult.Success;
    }
}
