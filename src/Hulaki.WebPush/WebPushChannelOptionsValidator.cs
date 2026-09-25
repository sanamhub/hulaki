using System;
using Microsoft.Extensions.Options;

namespace Hulaki.WebPush;

/// <summary>Checks named <see cref="WebPushChannelOptions"/> at startup, including that the VAPID keys are a pair. Never echoes a key.</summary>
internal sealed class WebPushChannelOptionsValidator : IValidateOptions<WebPushChannelOptions>
{
    public ValidateOptionsResult Validate(string? name, WebPushChannelOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var channel = name ?? string.Empty;
        if (options.TimeToLive < TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail($"Web Push channel '{channel}': TimeToLive must not be negative.");
        }

        using var signer = VapidSigner.TryCreate(options.VapidPublicKey, options.VapidPrivateKey, options.VapidSubject, TimeProvider.System, out var problem);
        return signer is null
            ? ValidateOptionsResult.Fail($"Web Push channel '{channel}': {problem}")
            : ValidateOptionsResult.Success;
    }
}
