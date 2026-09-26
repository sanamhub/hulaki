using System;
using Hulaki.Providers;
using Microsoft.Extensions.Options;

namespace Hulaki.Mastodon;

/// <summary>Checks named <see cref="MastodonChannelOptions"/> at startup. Never echoes the token.</summary>
internal sealed class MastodonChannelOptionsValidator : IValidateOptions<MastodonChannelOptions>
{
    public ValidateOptionsResult Validate(string? name, MastodonChannelOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var channel = name ?? string.Empty;
        if (!Endpoints.IsHttpsOrLoopback(options.InstanceUrl))
        {
            return ValidateOptionsResult.Fail($"Mastodon channel '{channel}': InstanceUrl must be an absolute HTTPS URL, or an HTTP loopback URL for a local server.");
        }

        if (string.IsNullOrWhiteSpace(options.AccessToken))
        {
            return ValidateOptionsResult.Fail($"Mastodon channel '{channel}': AccessToken is empty.");
        }

        if (options.Visibility is not (null or "public" or "unlisted" or "private" or "direct"))
        {
            return ValidateOptionsResult.Fail($"Mastodon channel '{channel}': Visibility must be public, unlisted, private or direct.");
        }

        return options.MaxCharacters is < 1
            ? ValidateOptionsResult.Fail($"Mastodon channel '{channel}': MaxCharacters must be at least 1.")
            : ValidateOptionsResult.Success;
    }
}
