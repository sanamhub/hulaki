using System;
using System.Net.Http.Headers;
using Hulaki.Providers;
using Microsoft.Extensions.Options;

namespace Hulaki.Webhook;

/// <summary>Checks named <see cref="WebhookChannelOptions"/> at startup. Never echoes the URL, the secret or a header value.</summary>
internal sealed class WebhookChannelOptionsValidator : IValidateOptions<WebhookChannelOptions>
{
    public ValidateOptionsResult Validate(string? name, WebhookChannelOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var channel = name ?? string.Empty;
        if (!Endpoints.IsHttpsOrLoopback(options.Url))
        {
            return ValidateOptionsResult.Fail($"Webhook channel '{channel}': Url must be an absolute HTTPS URL, or an HTTP loopback URL for a local receiver.");
        }

        if (!MediaTypeHeaderValue.TryParse(options.ContentType, out _))
        {
            return ValidateOptionsResult.Fail($"Webhook channel '{channel}': ContentType is not a media type.");
        }

        return options.MaxTextBytes < 1
            ? ValidateOptionsResult.Fail($"Webhook channel '{channel}': MaxTextBytes must be at least 1.")
            : ValidateOptionsResult.Success;
    }
}
