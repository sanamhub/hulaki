using System;
using System.Collections.Generic;
using Hulaki.Providers;

namespace Hulaki.Email;

/// <summary>
/// <c>smtp://[&lt;user&gt;[:&lt;password&gt;]@]&lt;host&gt;[:port]?from=&lt;address&gt;</c>, port 587 by
/// default. For the CLI only (ADR-0013).
/// </summary>
internal static class EmailChannelUrl
{
    public const string Scheme = "smtp";

    public const string Format = "smtp://[<user>[:<password>]@]<host>[:port]?from=<address>";

    public static IChannel? TryCreate(string name, Uri url, out string? error)
    {
        var (user, password) = ChannelUrlParts.Credentials(url);
        var options = new EmailChannelOptions
        {
            Host = url.Host,
            Port = url.IsDefaultPort || url.Port < 0 ? 587 : url.Port,
            Username = user.Length == 0 ? null : user,
            Password = password,
            From = ChannelUrlParts.Query(url).GetValueOrDefault("from", string.Empty),
        };
        return ChannelUrlParts.Create(name, options, new EmailChannelOptionsValidator(), () => new EmailChannel(name, options), out error);
    }
}
