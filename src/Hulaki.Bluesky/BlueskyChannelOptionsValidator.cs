using System;
using Hulaki.Providers;
using Microsoft.Extensions.Options;

namespace Hulaki.Bluesky;

/// <summary>Checks named <see cref="BlueskyChannelOptions"/> at startup. Never echoes the app password.</summary>
internal sealed class BlueskyChannelOptionsValidator : IValidateOptions<BlueskyChannelOptions>
{
    public ValidateOptionsResult Validate(string? name, BlueskyChannelOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var channel = name ?? string.Empty;
        if (string.IsNullOrWhiteSpace(options.Identifier))
        {
            return ValidateOptionsResult.Fail($"Bluesky channel '{channel}': Identifier is empty.");
        }

        if (string.IsNullOrWhiteSpace(options.AppPassword))
        {
            return ValidateOptionsResult.Fail($"Bluesky channel '{channel}': AppPassword is empty.");
        }

        return Endpoints.IsHttpsOrLoopback(options.ServiceUrl)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail($"Bluesky channel '{channel}': ServiceUrl must be an absolute HTTPS URL, or an HTTP loopback URL for a local server.");
    }
}
