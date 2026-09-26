using System;
using Hulaki.Providers;
using Microsoft.Extensions.Options;

namespace Hulaki.Ntfy;

/// <summary>Checks named <see cref="NtfyChannelOptions"/> at startup. Never echoes the token.</summary>
internal sealed class NtfyChannelOptionsValidator : IValidateOptions<NtfyChannelOptions>
{
    public ValidateOptionsResult Validate(string? name, NtfyChannelOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return Endpoints.IsHttpsOrLoopback(options.BaseAddress)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail($"ntfy channel '{name}': BaseAddress must be an absolute HTTPS URL, or an HTTP loopback URL for a local server.");
    }
}
