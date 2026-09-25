using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Hulaki.Providers;

/// <summary>
/// Reads the parts of a channel URL such as <c>telegram://token@telegram</c>, for the CLI's
/// <c>--channel-url</c> (ADR-0013). Secrets sit in the user info or the query, which nothing
/// prints: <see cref="Redacted"/> keeps the scheme and host only.
/// </summary>
/// <remarks>Compiled into every provider assembly from <c>src/Shared</c>.</remarks>
internal static class ChannelUrlParts
{
    /// <summary>The whole user info, percent-decoded. Empty when there is none.</summary>
    public static string UserInfo(Uri url) => Uri.UnescapeDataString(url.UserInfo);

    /// <summary>User info split at its first colon, both halves percent-decoded.</summary>
    public static (string User, string? Password) Credentials(Uri url)
    {
        var colon = url.UserInfo.IndexOf(':', StringComparison.Ordinal);
        return colon < 0
            ? (Uri.UnescapeDataString(url.UserInfo), null)
            : (Uri.UnescapeDataString(url.UserInfo[..colon]), Uri.UnescapeDataString(url.UserInfo[(colon + 1)..]));
    }

    /// <summary>The query as case-insensitive keys, values percent-decoded. A repeated key keeps its last value.</summary>
    public static Dictionary<string, string> Query(Uri url)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = pair.IndexOf('=', StringComparison.Ordinal);
            var key = Uri.UnescapeDataString(equals < 0 ? pair : pair[..equals]);
            values[key] = equals < 0 ? string.Empty : Uri.UnescapeDataString(pair[(equals + 1)..]);
        }

        return values;
    }

    /// <summary>The URL with only its scheme and host: safe to print.</summary>
    public static string Redacted(Uri url) => url.IsAbsoluteUri ? $"{url.Scheme}://{url.Host}" : "(not a URL)";

    /// <summary>
    /// Runs the provider's startup validator on <paramref name="options"/>, then creates the
    /// channel. Validator messages never hold a secret; constructor messages are replaced anyway.
    /// </summary>
    public static IChannel? Create<TOptions>(string name, TOptions options, IValidateOptions<TOptions> validator, Func<IChannel> create, out string? error)
        where TOptions : class
    {
        var result = validator.Validate(name, options);
        if (result.Failed)
        {
            error = result.FailureMessage;
            return null;
        }

        try
        {
            error = null;
            return create();
        }
        catch (ArgumentException)
        {
            error = "The channel URL does not describe a valid channel.";
            return null;
        }
    }
}
