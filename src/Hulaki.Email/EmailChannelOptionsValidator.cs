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
        if (!EmailChannel.TryParseAddress(options.From, out _))
        {
            return ValidateOptionsResult.Fail($"Email channel '{channel}': From is not an email address.");
        }

        if (options.UseFreeTierMail)
        {
            // Settings that would be ignored are a mistake to point out, not to skip.
            return string.IsNullOrEmpty(options.Host) && string.IsNullOrEmpty(options.Username) && string.IsNullOrEmpty(options.Password)
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail($"Email channel '{channel}': UseFreeTierMail is true, so Host, Username and Password are unused. Configure the server as a FreeTierMail SMTP provider instead.");
        }

        if (string.IsNullOrWhiteSpace(options.Host))
        {
            return ValidateOptionsResult.Fail($"Email channel '{channel}': Host is empty.");
        }

        if (options.Port is < 1 or > 65535)
        {
            return ValidateOptionsResult.Fail($"Email channel '{channel}': Port must be between 1 and 65535.");
        }

        if (string.IsNullOrEmpty(options.Username))
        {
            if (!string.IsNullOrEmpty(options.Password))
            {
                return ValidateOptionsResult.Fail($"Email channel '{channel}': Password is set without a Username.");
            }

            return IsLoopback(options.Host)
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail($"Email channel '{channel}': Username is empty; only a loopback host may skip the login.");
        }

        return string.IsNullOrEmpty(options.Password)
            ? ValidateOptionsResult.Fail($"Email channel '{channel}': Username is set without a Password.")
            : ValidateOptionsResult.Success;
    }

    private static bool IsLoopback(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || (System.Net.IPAddress.TryParse(host, out var address) && System.Net.IPAddress.IsLoopback(address));
}
