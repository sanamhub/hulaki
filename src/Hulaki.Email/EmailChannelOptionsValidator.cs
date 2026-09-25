using System;
using Microsoft.Extensions.Options;

namespace Hulaki.Email;

/// <summary>Checks named <see cref="EmailChannelOptions"/> at startup. Never echoes the password or an address.</summary>
internal sealed class EmailChannelOptionsValidator : IValidateOptions<EmailChannelOptions>
{
    public ValidateOptionsResult Validate(string? name, EmailChannelOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var channel = name ?? string.Empty;
        if (string.IsNullOrWhiteSpace(options.Host))
        {
            return ValidateOptionsResult.Fail($"Email channel '{channel}': Host is empty.");
        }

        if (options.Port is < 1 or > 65535)
        {
            return ValidateOptionsResult.Fail($"Email channel '{channel}': Port must be between 1 and 65535.");
        }

        if (!EmailChannel.TryParseAddress(options.From, out _))
        {
            return ValidateOptionsResult.Fail($"Email channel '{channel}': From is not an email address.");
        }

        return string.IsNullOrEmpty(options.Username) && !string.IsNullOrEmpty(options.Password)
            ? ValidateOptionsResult.Fail($"Email channel '{channel}': Password is set without a Username.")
            : ValidateOptionsResult.Success;
    }
}
