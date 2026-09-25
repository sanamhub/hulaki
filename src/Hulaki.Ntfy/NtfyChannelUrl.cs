using System;
using System.Net.Http;
using Hulaki.Providers;

namespace Hulaki.Ntfy;

/// <summary>
/// <c>ntfy://[&lt;access-token&gt;@]&lt;host&gt;[:port][/path]</c>. HTTPS, except a loopback host,
/// which is reached over HTTP. For the CLI only (ADR-0013).
/// </summary>
internal static class NtfyChannelUrl
{
    public const string Scheme = "ntfy";

    public const string Format = "ntfy://[<access-token>@]<host>[:port][/path]";

    public static IChannel? TryCreate(string name, Uri url, HttpClient http, out string? error)
    {
        var token = ChannelUrlParts.UserInfo(url);
        var options = new NtfyChannelOptions
        {
            BaseAddress = new UriBuilder(url.IsLoopback ? Uri.UriSchemeHttp : Uri.UriSchemeHttps, url.Host, url.IsDefaultPort ? -1 : url.Port, url.AbsolutePath).Uri,
            AccessToken = token.Length == 0 ? null : token,
        };
        return ChannelUrlParts.Create(name, options, new NtfyChannelOptionsValidator(), () => new NtfyChannel(name, http, options), out error);
    }
}
